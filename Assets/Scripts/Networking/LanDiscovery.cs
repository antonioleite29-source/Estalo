using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Finds games on the local Wi-Fi so nobody has to type an IP address.
//
// The host shouts a short message onto the network a few times a second; every phone on the same
// Wi-Fi hears it and learns the host's address. Typing an IP was the single worst part of the
// join flow — it needs the tester to find the host's address, read it out, and type it correctly
// on a phone keyboard, and any one of those going wrong looks identical to the game being broken.
//
// Deliberately UDP broadcast rather than multicast: Android needs a MulticastLock for multicast
// but not for broadcast, and broadcast is what home routers pass by default.
public class LanDiscovery : MonoBehaviour
{
    // Separate from the game port (7777) so a host can advertise and play at the same time.
    public const int DiscoveryPort = 47777;

    // Prefix so we ignore whatever else happens to be broadcasting on this port.
    private const string Magic = "TRIVIADUEL1";

    // A searching client sends this; a host answers it directly. Relying on the host's broadcasts
    // alone was not enough: Android needs a MulticastLock to reliably receive packets sent to
    // 255.255.255.255, so a phone opened before the host could sit there hearing nothing forever.
    // A host's *reply* is unicast, which Android receives without any of that.
    private const string QueryToken = "WHO";

    private const float BroadcastIntervalSeconds = 1f;

    // A host that stops shouting is assumed gone. Three missed beats rather than one, so a single
    // dropped packet on a busy network doesn't make a game flicker out of the list.
    private const float HostTimeoutSeconds = 3.5f;

    public struct FoundHost
    {
        public string Address;
        public int Port;
        public string Name;
        public float LastSeen;
    }

    public event Action<FoundHost> HostFound;

    private UdpClient broadcastClient;
    private UdpClient listenClient;
    private Thread listenThread;
    private volatile bool listening;

    private int advertisedGamePort;
    private string advertisedName = "Trivia Duel";
    private float nextBroadcastTime;
    private bool advertising;

    // Written by the listen thread, drained on the main thread: Unity API and C# events must not
    // be touched from a background thread.
    private readonly Queue<FoundHost> pendingHosts = new Queue<FoundHost>();
    private readonly Dictionary<string, FoundHost> knownHosts = new Dictionary<string, FoundHost>();

    public void StartAdvertising(int gamePort, string hostName)
    {
        advertisedGamePort = gamePort;
        advertisedName = string.IsNullOrEmpty(hostName) ? "Trivia Duel" : hostName;

        try
        {
            if (!TryOpenBroadcastSocket())
                return;

            advertising = true;
            nextBroadcastTime = 0f;

            // A host listens too, so it can answer clients that ask for games directly. Without
            // this it only ever shouts, and a phone that cannot hear broadcasts never finds it.
            StartSearching();

            Debug.Log($"LAN discovery: advertising '{advertisedName}' on port {DiscoveryPort}.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("LAN discovery: could not start advertising, players will have to " +
                             "type the IP instead. " + e.Message);
            advertising = false;
        }
    }

    public void StartSearching()
    {
        if (listening)
            return;

        try
        {
            listenClient = new UdpClient();

            // ReuseAddress so the host can also listen on this port — otherwise a machine that is
            // hosting cannot see other games, and two virtual players on one machine collide.
            listenClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listenClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            listening = true;
            listenThread = new Thread(ListenLoop) { IsBackground = true };
            listenThread.Start();

            Debug.Log("LAN discovery: searching for games on this Wi-Fi.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("LAN discovery: could not start searching. " + e.Message);
            listening = false;
        }
    }

    public void StopAll()
    {
        advertising = false;
        listening = false;

        // Closing the socket is what unblocks the thread's Receive call; without this the thread
        // sits on Receive until the app quits.
        listenClient?.Close();
        listenClient = null;

        broadcastClient?.Close();
        broadcastClient = null;

        listenThread = null;
    }

    private void ListenLoop()
    {
        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);

        while (listening)
        {
            try
            {
                byte[] data = listenClient.Receive(ref from);
                string message = Encoding.UTF8.GetString(data);
                string[] parts = message.Split('|');

                if (parts.Length < 2 || parts[0] != Magic)
                    continue;

                // Someone is looking for games. If we are hosting one, answer them directly rather
                // than waiting for our next broadcast — that reply is what phones can actually hear.
                if (parts[1] == QueryToken)
                {
                    if (advertising)
                        AnswerQuery(from);

                    continue;
                }

                if (parts.Length < 3)
                    continue;

                if (!int.TryParse(parts[1], out int port))
                    continue;

                FoundHost host = new FoundHost
                {
                    Address = from.Address.ToString(),
                    Port = port,
                    Name = parts[2]
                };

                lock (pendingHosts)
                    pendingHosts.Enqueue(host);
            }
            catch (SocketException)
            {
                // Expected when StopAll closes the socket out from under Receive.
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextBroadcastTime)
        {
            nextBroadcastTime = Time.unscaledTime + BroadcastIntervalSeconds;

            if (advertising)
                Broadcast();

            // Keep asking, not just once on open: the host may not exist yet when the phone is the
            // first thing switched on, which is exactly the case that used to never connect.
            if (listening && !advertising)
                SendQuery();
        }

        DrainPendingHosts();
        ForgetStaleHosts();
    }

    private void SendQuery()
    {
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes($"{Magic}|{QueryToken}");
            listenClient.EnableBroadcast = true;
            listenClient.Send(payload, payload.Length,
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch (Exception)
        {
            // Sending can fail transiently while Wi-Fi is still coming up. The next tick retries,
            // so there is nothing useful to say here and a log line every second would be noise.
        }
    }

    private void AnswerQuery(IPEndPoint asker)
    {
        // Closed from the main thread by StopAll while this runs on the listen thread.
        if (listenClient == null)
            return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(
                $"{Magic}|{advertisedGamePort}|{advertisedName}");

            listenClient.Send(payload, payload.Length, asker);
        }
        catch (Exception e)
        {
            Debug.LogWarning("LAN discovery: could not answer a search. " + e.Message);
        }
    }

    private void Broadcast()
    {
        // The socket can be gone while advertising is still true — a script recompile mid-session
        // rebuilds private fields without re-running the setup that made it. Rebuilding it beats
        // giving up: this used to switch discovery off for the rest of the session on one null,
        // and the only symptom players saw was that nobody could find their game.
        if (broadcastClient == null && !TryOpenBroadcastSocket())
            return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes($"{Magic}|{advertisedGamePort}|{advertisedName}");
            broadcastClient.Send(payload, payload.Length,
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch (Exception e)
        {
            // Wi-Fi dropping or switching networks throws here and recovers on its own, so the
            // socket is dropped and reopened on the next tick rather than ending advertising.
            Debug.LogWarning("LAN discovery: broadcast failed, retrying next tick. " + e.Message);

            broadcastClient?.Close();
            broadcastClient = null;
        }
    }

    private bool TryOpenBroadcastSocket()
    {
        try
        {
            broadcastClient = new UdpClient { EnableBroadcast = true };
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("LAN discovery: could not open the broadcast socket, players will have " +
                             "to type the IP instead. " + e.Message);
            advertising = false;
            return false;
        }
    }

    private void DrainPendingHosts()
    {
        lock (pendingHosts)
        {
            while (pendingHosts.Count > 0)
            {
                FoundHost host = pendingHosts.Dequeue();
                host.LastSeen = Time.unscaledTime;

                bool isNew = !knownHosts.ContainsKey(host.Address);
                knownHosts[host.Address] = host;

                if (isNew)
                {
                    Debug.Log($"LAN discovery: found '{host.Name}' at {host.Address}:{host.Port}.");
                    HostFound?.Invoke(host);
                }
            }
        }
    }

    private void ForgetStaleHosts()
    {
        if (knownHosts.Count == 0)
            return;

        List<string> expired = null;

        foreach (KeyValuePair<string, FoundHost> entry in knownHosts)
        {
            if (Time.unscaledTime - entry.Value.LastSeen > HostTimeoutSeconds)
                (expired ??= new List<string>()).Add(entry.Key);
        }

        if (expired == null)
            return;

        foreach (string address in expired)
            knownHosts.Remove(address);
    }

    private void OnDestroy()
    {
        StopAll();
    }

    private void OnApplicationQuit()
    {
        StopAll();
    }
}
