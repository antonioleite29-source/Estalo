// Renders the Estalo trailer: 1080 x 1920, thirty frames a second, H.264 with the narration and the
// game's own music under it. Nothing but what comes with macOS.
//
//   swift Tools/Video/trailer.swift
//
// The pictures are drawn here from the game's real art -- the duel board, the button states, the
// avatars, the logo and the Inter faces -- rather than captured from the game, because Unity cannot be
// driven frame by frame from outside. So this is a trailer, not footage: every question, score and
// screen in it is real, but it is drawn, and a demo that has to show the app running needs a screen
// recording instead.
import AVFoundation
import CoreGraphics
import CoreText
import Foundation
import ImageIO

let W = 1080, H = 1920, FPS: Int32 = 30
let root = FileManager.default.currentDirectoryPath
let art = root + "/Assets/Art"
let voiceDir = root + "/Tools/Video/voice"
let outURL = URL(fileURLWithPath: root + "/Tools/Video/estalo-trailer.mp4")

// --- the game's own colours ------------------------------------------------
let blue = CGColor(red: 0.42, green: 0.63, blue: 0.72, alpha: 1)      // side 1 on the board
let red = CGColor(red: 0.81, green: 0.44, blue: 0.42, alpha: 1)       // side 2
let panel = CGColor(red: 0.47, green: 0.49, blue: 0.66, alpha: 1)     // the question bubble
let ink = CGColor(gray: 1, alpha: 1)
let green = CGColor(red: 0.22, green: 0.70, blue: 0.47, alpha: 1)

func image(_ path: String) -> CGImage? {
    guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil) else { return nil }
    return CGImageSourceCreateImageAtIndex(src, 0, nil)
}

func font(_ file: String, _ size: CGFloat) -> CTFont {
    guard let data = NSData(contentsOfFile: root + "/Assets/Fonts/" + file),
          let provider = CGDataProvider(data: data),
          let cg = CGFont(provider) else { return CTFontCreateWithName("Helvetica-Bold" as CFString, size, nil) }
    return CTFontCreateWithGraphicsFont(cg, size, nil, nil)
}

let black = font("Inter/Inter-Black.ttf", 1)
let semi = font("Inter/Inter-SemiBold.ttf", 1)

// Art, loaded once.
let boardImage = image(art + "/Backgrounds/Backgounrd1x1final.png")
let lobbyImage = image(art + "/Backgrounds/Lobby.png")
let logoImage = image(art + "/Logo/EstaloLogo.png") ?? image(art + "/Logo/EstaloLogoCirculo.png")
let buttonNormal = image(art + "/UI/ButtonStates/NormalState.png") ?? image(art + "/UI/NotPressed.png")
let buttonRight = image(art + "/UI/ButtonStates/PressedRight.png") ?? image(art + "/UI/PressedRight.png")
let buttonWrong = image(art + "/UI/ButtonStates/PressedWrong.png") ?? image(art + "/UI/PressedWrong.png")
let pathImage = image(art + "/UI/LearningPath/NewPath1.png")
let avatarA = image(root + "/Assets/Art/Avatars/Robo1.png")
let avatarB = image(root + "/Assets/Art/Avatars/Blob3.png")

// --- drawing helpers -------------------------------------------------------
extension CGContext {
    func fill(_ rect: CGRect, _ colour: CGColor) {
        setFillColor(colour)
        fill(rect)
    }

    func rounded(_ rect: CGRect, _ radius: CGFloat, _ colour: CGColor) {
        setFillColor(colour)
        addPath(CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil))
        fillPath()
    }

    func draw(_ img: CGImage?, fitting rect: CGRect) {
        guard let img = img else { return }
        let scale = min(rect.width / CGFloat(img.width), rect.height / CGFloat(img.height))
        let size = CGSize(width: CGFloat(img.width) * scale, height: CGFloat(img.height) * scale)
        draw(img, in: CGRect(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2,
                             width: size.width, height: size.height))
    }

    // Covers the rect rather than fitting inside it: a background has to reach the edges of the frame,
    // and the board art is a different shape from 1080 x 1920.
    func draw(_ img: CGImage?, covering rect: CGRect) {
        guard let img = img else { return }
        let scale = max(rect.width / CGFloat(img.width), rect.height / CGFloat(img.height))
        let size = CGSize(width: CGFloat(img.width) * scale, height: CGFloat(img.height) * scale)
        draw(img, in: CGRect(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2,
                             width: size.width, height: size.height))
    }

    // One line of centred text, shrinking until it fits the width it was given.
    func text(_ string: String, _ ctFont: CTFont, _ size: CGFloat, at centre: CGPoint,
              width: CGFloat, colour: CGColor = CGColor(gray: 1, alpha: 1), alpha: CGFloat = 1) {
        var points = size

        while points > 10 {
            let f = CTFontCreateCopyWithAttributes(ctFont, points, nil, nil)
            let attr = NSAttributedString(string: string, attributes: [
                kCTFontAttributeName as NSAttributedString.Key: f,
                kCTForegroundColorAttributeName as NSAttributedString.Key: colour.copy(alpha: colour.alpha * alpha) ?? colour,
            ])
            let line = CTLineCreateWithAttributedString(attr)
            let bounds = CTLineGetBoundsWithOptions(line, .useOpticalBounds)

            if bounds.width <= width || points <= 11 {
                textPosition = CGPoint(x: centre.x - bounds.width / 2, y: centre.y - bounds.height / 2 - bounds.minY)
                CTLineDraw(line, self)
                return
            }

            points -= 2
        }
    }

    // Several lines, wrapped on words, centred on a point.
    func paragraph(_ string: String, _ ctFont: CTFont, _ size: CGFloat, at centre: CGPoint,
                   width: CGFloat, lineGap: CGFloat = 1.25, colour: CGColor = CGColor(gray: 1, alpha: 1),
                   alpha: CGFloat = 1) {
        let f = CTFontCreateCopyWithAttributes(ctFont, size, nil, nil)
        var lines: [String] = []
        var current = ""

        for word in string.split(separator: " ") {
            let candidate = current.isEmpty ? String(word) : current + " " + word
            let attr = NSAttributedString(string: candidate, attributes: [kCTFontAttributeName as NSAttributedString.Key: f])
            let w = CTLineGetBoundsWithOptions(CTLineCreateWithAttributedString(attr), .useOpticalBounds).width

            if w > width && !current.isEmpty {
                lines.append(current)
                current = String(word)
            } else {
                current = candidate
            }
        }

        if !current.isEmpty { lines.append(current) }

        let step = size * lineGap
        var y = centre.y + step * CGFloat(lines.count - 1) / 2

        for line in lines {
            text(line, ctFont, size, at: CGPoint(x: centre.x, y: y), width: width, colour: colour, alpha: alpha)
            y -= step
        }
    }
}

func ease(_ t: CGFloat) -> CGFloat {
    let c = max(0, min(1, t))
    return c < 0.5 ? 4 * c * c * c : 1 - pow(-2 * c + 2, 3) / 2
}

func fadeIn(_ t: Double, _ start: Double, _ length: Double = 0.4) -> CGFloat {
    ease(CGFloat((t - start) / length))
}

// --- the board -------------------------------------------------------------
// Laid out like the real 1v1 screen: question bubble at the top, the score under it, four answers
// across the middle, two players at the bottom.
struct Board {
    var question = ""
    var answers: [String] = []
    var rightIndex = 0
    var chosen: Int? = nil
    var chosenIsRight = true
    var scoreLeft = 0
    var scoreRight = 0
    var soloFill: CGFloat? = nil      // 0...1 while the solo ring is running
    var soloOnLeft = true
    var reveal: CGFloat = 1           // answers fading in
}

func drawBoard(_ ctx: CGContext, _ b: Board) {
    ctx.draw(boardImage, covering: CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)))

    // Question bubble.
    let bubble = CGRect(x: 40, y: CGFloat(H) - 560, width: CGFloat(W) - 80, height: 420)
    ctx.rounded(bubble, 36, panel)
    ctx.paragraph(b.question, black, 74, at: CGPoint(x: bubble.midX, y: bubble.midY), width: bubble.width - 90)

    // Score, in a tab under the bubble.
    let tab = CGRect(x: CGFloat(W) / 2 - 170, y: bubble.minY - 130, width: 340, height: 130)
    ctx.rounded(tab, 24, panel)
    ctx.text("\(b.scoreLeft)", black, 86, at: CGPoint(x: tab.midX - 70, y: tab.midY), width: 120)
    ctx.text("\(b.scoreRight)", black, 86, at: CGPoint(x: tab.midX + 70, y: tab.midY), width: 120, colour: red)

    // Answers.
    let tray = CGRect(x: 50, y: CGFloat(H) * 0.47, width: CGFloat(W) - 100, height: 250)
    ctx.rounded(tray, 30, panel.copy(alpha: 0.85) ?? panel)

    let gap: CGFloat = 18
    let cell = (tray.width - gap * 5) / 4

    for (i, answer) in b.answers.enumerated() {
        let rect = CGRect(x: tray.minX + gap + CGFloat(i) * (cell + gap), y: tray.midY - cell / 2,
                          width: cell, height: cell)
        var sprite = buttonNormal

        if let chosen = b.chosen {
            if i == chosen { sprite = b.chosenIsRight ? buttonRight : buttonWrong }
            else if i == b.rightIndex && !b.chosenIsRight { sprite = buttonRight }
        }

        // The untouched buttons are near-white, so their labels are dark; a green or red one takes
        // white, the same way the game does it.
        let pressed = b.chosen != nil && (i == b.chosen || (i == b.rightIndex && !b.chosenIsRight))
        let label = pressed ? ink : CGColor(red: 0.13, green: 0.15, blue: 0.19, alpha: 1)

        ctx.saveGState()
        ctx.setAlpha(b.reveal)
        ctx.draw(sprite, fitting: rect)
        ctx.text(answer, black, 58, at: CGPoint(x: rect.midX, y: rect.midY), width: rect.width - 24,
                 colour: label, alpha: b.reveal)
        ctx.restoreGState()
    }

    // The two players.
    for (i, avatar) in [avatarA, avatarB].enumerated() {
        let left = i == 0
        let centre = CGPoint(x: left ? 300 : CGFloat(W) - 300, y: 520)
        let frame = CGRect(x: centre.x - 110, y: centre.y - 110, width: 220, height: 220)
        ctx.rounded(frame.insetBy(dx: -18, dy: -18), 28, panel)
        ctx.draw(avatar, fitting: frame)
        ctx.text(left ? "Você" : "Marina", black, 48, at: CGPoint(x: centre.x, y: centre.y - 165), width: 260)

        // The solo ring, drawn round whoever has the four seconds.
        if let fill = b.soloFill, left == b.soloOnLeft {
            ctx.setLineWidth(16)
            ctx.setStrokeColor(left ? blue : red)
            ctx.addArc(center: centre, radius: 150, startAngle: .pi / 2,
                       endAngle: .pi / 2 - 2 * .pi * Double(fill), clockwise: true)
            ctx.strokePath()
        }
    }
}

// --- scenes ----------------------------------------------------------------
// Each scene is a closure given its own time, in seconds, and the frame to draw on.
struct Scene {
    let length: Double
    let draw: (CGContext, Double) -> Void
}

// Which line plays over which scene, in order.
let voiceOrder = ["v1", "v7", "v2", "v3", "v8", "v4", "v5", "v6"]

func voiceLength(_ name: String) -> Double {
    let url = URL(fileURLWithPath: voiceDir + "/" + name + ".wav")
    guard let file = try? AVAudioFile(forReading: url) else { return 4 }
    return Double(file.length) / file.fileFormat.sampleRate
}

let q1 = "Quanto é 7 × 8?"
let a1 = ["54", "56", "48", "63"]
let q2 = "Qual é o volume de um cubo de 4 cm?"
let a2 = ["16 cm³", "64 cm³", "12 cm³", "48 cm³"]

var scenes: [Scene] = []

// 1. The logo.
scenes.append(Scene(length: voiceLength("v1") + 0.8) { ctx, t in
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)), panel)
    let rise = ease(CGFloat(t / 1.2))
    ctx.saveGState()
    ctx.setAlpha(rise)
    ctx.draw(logoImage, fitting: CGRect(x: 140, y: CGFloat(H) / 2 - 320 + (1 - rise) * 40,
                                        width: CGFloat(W) - 280, height: 640))
    ctx.restoreGState()
    ctx.text("Acerte primeiro.", black, 76, at: CGPoint(x: CGFloat(W) / 2, y: CGFloat(H) / 2 - 420),
             width: CGFloat(W) - 200, alpha: fadeIn(t, 1.0, 0.6))
})

// 2. The tutorial.
scenes.append(Scene(length: voiceLength("v7") + 1.2) { ctx, t in
    ctx.draw(boardImage, covering: CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)))

    let card = CGRect(x: 60, y: CGFloat(H) / 2 - 300, width: CGFloat(W) - 120, height: 600)
    ctx.saveGState()
    ctx.setAlpha(fadeIn(t, 0.2, 0.5))
    ctx.rounded(card, 36, panel)
    ctx.paragraph("Você sempre será do time azul, e os pontos do seu time são os do seu lado.",
                  black, 70, at: CGPoint(x: card.midX, y: card.midY + 40), width: card.width - 100)
    ctx.text("Tutorial", semi, 46, at: CGPoint(x: card.midX, y: card.minY + 80), width: card.width - 100,
             colour: CGColor(red: 0.72, green: 0.76, blue: 0.84, alpha: 1))
    ctx.restoreGState()

    if t > 3.6 {
        ctx.text("Seu IQ inicial é 112 — nível 4 de 7.", black, 58,
                 at: CGPoint(x: CGFloat(W) / 2, y: 300), width: CGFloat(W) - 160, alpha: fadeIn(t, 3.6, 0.5))
    }
})

// 3. A question, answered first.
scenes.append(Scene(length: voiceLength("v2") + 3.2) { ctx, t in
    var b = Board()
    b.question = q1
    b.answers = a1
    b.rightIndex = 1
    b.reveal = fadeIn(t, 0.1, 0.5)
    b.scoreLeft = t > 2.6 ? 1 : 0

    if t > 2.1 {
        b.chosen = 1
        b.chosenIsRight = true
    }

    drawBoard(ctx, b)

    if t > 2.8 {
        ctx.text("+1", black, 90, at: CGPoint(x: 300, y: 760), width: 200, colour: green,
                 alpha: fadeIn(t, 2.8, 0.3))
    }
})

// 3. The solo rule.
scenes.append(Scene(length: voiceLength("v3") + 3.0) { ctx, t in
    var b = Board()
    b.question = q2
    b.answers = a2
    b.rightIndex = 1
    b.scoreLeft = 1
    b.scoreRight = 0

    if t > 1.0 {
        b.chosen = 3
        b.chosenIsRight = false
    }

    if t > 1.8 {
        b.soloFill = max(0, 1 - CGFloat((t - 1.8) / 4.0))
        b.soloOnLeft = false
    }

    if t > 6.0 {
        b.scoreRight = 1
        b.chosen = 1
        b.chosenIsRight = true
    }

    drawBoard(ctx, b)

    if t > 2.0 {
        let band = CGRect(x: 0, y: 120, width: CGFloat(W), height: 160)
        ctx.saveGState()
        ctx.setAlpha(min(1, fadeIn(t, 2.0, 0.4)))
        ctx.fill(band, CGColor(gray: 0, alpha: 0.55))
        ctx.text("Errou? O outro responde sozinho.", black, 66, at: CGPoint(x: band.midX, y: band.midY),
                 width: band.width - 120)
        ctx.restoreGState()
    }
})

// 5. Two against two.
scenes.append(Scene(length: voiceLength("v8") + 1.2) { ctx, t in
    ctx.draw(image(art + "/Backgrounds/Background2x2final.png") ?? boardImage,
             covering: CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)))

    let bubble = CGRect(x: 40, y: CGFloat(H) - 520, width: CGFloat(W) - 80, height: 380)
    ctx.rounded(bubble, 36, panel)
    ctx.paragraph("Quanto é 15% de 240?", black, 74, at: CGPoint(x: bubble.midX, y: bubble.midY),
                  width: bubble.width - 90)

    // The answers, so the board is not an empty tray while the voice talks about it.
    let tray = CGRect(x: 50, y: CGFloat(H) * 0.52, width: CGFloat(W) - 100, height: 250)
    ctx.rounded(tray, 30, panel.copy(alpha: 0.85) ?? panel)

    let answers = ["24", "36", "40", "32"]
    let gap: CGFloat = 18
    let cell = (tray.width - gap * 5) / 4

    for (i, answer) in answers.enumerated() {
        let rect = CGRect(x: tray.minX + gap + CGFloat(i) * (cell + gap), y: tray.midY - cell / 2,
                          width: cell, height: cell)
        let right = i == 1 && t > 3.4
        ctx.draw(right ? buttonRight : buttonNormal, fitting: rect)
        ctx.text(answer, black, 58, at: CGPoint(x: rect.midX, y: rect.midY), width: rect.width - 24,
                 colour: right ? ink : CGColor(red: 0.13, green: 0.15, blue: 0.19, alpha: 1))
    }

    // Four seats, and the bar over the pair whose turn it is -- the rule the 2v2 tutorial teaches.
    let seats: [(CGFloat, CGFloat, String, CGImage?)] = [
        (250, 700, "Você", avatarA), (250, 330, "Rafa", image(root + "/Assets/Art/Avatars/Robo4.png")),
        (CGFloat(W) - 250, 700, "Marina", avatarB), (CGFloat(W) - 250, 330, "Caio", image(root + "/Assets/Art/Avatars/Blob5.png")),
    ]

    let active = Int(t / 2.2) % 2      // the turn swapping between team-mates

    for (i, seat) in seats.enumerated() {
        let centre = CGPoint(x: seat.0, y: seat.1)
        let frame = CGRect(x: centre.x - 95, y: centre.y - 95, width: 190, height: 190)
        ctx.rounded(frame.insetBy(dx: -16, dy: -16), 26, panel)
        ctx.draw(seat.3, fitting: frame)
        ctx.text(seat.2, black, 44, at: CGPoint(x: centre.x, y: centre.y - 145), width: 240)

        if i % 2 == active {
            ctx.rounded(CGRect(x: centre.x - 60, y: centre.y + 120, width: 120, height: 18), 9,
                        i < 2 ? blue : red)
        }
    }

    ctx.text("2 x 2: um de cada time responde por rodada", black, 56,
             at: CGPoint(x: CGFloat(W) / 2, y: 120), width: CGFloat(W) - 120, alpha: fadeIn(t, 1.0, 0.6))
})

// 6. The learning path.
scenes.append(Scene(length: voiceLength("v4") + 1.6) { ctx, t in
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)), blue)
    let scroll = CGFloat(t) * 90
    ctx.draw(pathImage, covering: CGRect(x: 0, y: -400 + scroll, width: CGFloat(W), height: CGFloat(H) + 400))

    let lessons = ["Adição", "Sequências", "Subtração", "Geometria", "Multiplicação"]

    for (i, title) in lessons.enumerated() {
        let y = CGFloat(H) - 420 - CGFloat(i) * 330 + scroll
        if y < -200 || y > CGFloat(H) + 200 { continue }
        let x = CGFloat(W) * (i % 2 == 0 ? 0.32 : 0.68)
        let done = i < 2
        ctx.rounded(CGRect(x: x - 85, y: y - 85, width: 170, height: 170), 85, done ? green : panel)
        ctx.text("\(i + 1)", black, 72, at: CGPoint(x: x, y: y), width: 140)
        ctx.text(title, semi, 44, at: CGPoint(x: x, y: y - 125), width: 340)
    }

    ctx.text("20 lições, da adição às equações", black, 60,
             at: CGPoint(x: CGFloat(W) / 2, y: 160), width: CGFloat(W) - 140, alpha: fadeIn(t, 1.2, 0.6))
})

// 5. The stats.
scenes.append(Scene(length: voiceLength("v5") + 1.4) { ctx, t in
    ctx.draw(lobbyImage, covering: CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)))
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)), CGColor(gray: 0, alpha: 0.25))

    let card = CGRect(x: 80, y: CGFloat(H) / 2 - 520, width: CGFloat(W) - 160, height: 1040)
    ctx.rounded(card, 40, panel.copy(alpha: 0.95) ?? panel)
    ctx.text("Estatísticas", black, 86, at: CGPoint(x: card.midX, y: card.maxY - 120), width: card.width - 120)

    let rows = [("Partidas jogadas", "24"), ("Vitórias", "15"), ("Derrotas", "9"),
                ("Pontos máximos", "118.40"), ("Pontos atuais", "112.75"),
                ("Jogando há", "2 meses e 6 dias"), ("Ranking global", "#3 de 41")]

    for (i, row) in rows.enumerated() {
        let y = card.maxY - 290 - CGFloat(i) * 120
        let appear = fadeIn(t, 0.4 + Double(i) * 0.18, 0.3)
        ctx.text(row.0, semi, 50, at: CGPoint(x: card.minX + 230, y: y), width: 420,
                 colour: CGColor(red: 0.66, green: 0.70, blue: 0.76, alpha: 1), alpha: appear)
        ctx.text(row.1, black, 56, at: CGPoint(x: card.maxX - 230, y: y), width: 420, alpha: appear)
    }
})

// 6. The close.
scenes.append(Scene(length: voiceLength("v6") + 1.6) { ctx, t in
    ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)), panel)
    ctx.draw(logoImage, fitting: CGRect(x: 160, y: CGFloat(H) / 2 - 300, width: CGFloat(W) - 320, height: 600))
    ctx.text("em breve no Google Play", black, 62, at: CGPoint(x: CGFloat(W) / 2, y: CGFloat(H) / 2 - 420),
             width: CGFloat(W) - 200, alpha: fadeIn(t, 0.8, 0.8))
})

let total = scenes.reduce(0) { $0 + $1.length }
let frameCount = Int(total * Double(FPS))
print(String(format: "trailer: %.1f s, %d frames", total, frameCount))

// --- the soundtrack --------------------------------------------------------
// Narration where each line's scene starts, with the lobby loop underneath it at a quarter the volume.
func buildAudio() throws -> URL {
    let composition = AVMutableComposition()
    let speech = composition.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)!
    let music = composition.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)!

    var at = CMTime.zero

    for (i, scene) in scenes.enumerated() {
        let url = URL(fileURLWithPath: voiceDir + "/" + (i < voiceOrder.count ? voiceOrder[i] : "none") + ".wav")

        if FileManager.default.fileExists(atPath: url.path) {
            let asset = AVURLAsset(url: url)

            if let track = asset.tracks(withMediaType: .audio).first {
                // A beat of air before each line, so it does not start on the cut.
                let start = CMTimeAdd(at, CMTime(seconds: 0.35, preferredTimescale: 600))
                try speech.insertTimeRange(CMTimeRange(start: .zero, duration: asset.duration),
                                           of: track, at: start)
            }
        }

        at = CMTimeAdd(at, CMTime(seconds: scene.length, preferredTimescale: 600))
    }

    let musicURL = URL(fileURLWithPath: root + "/Assets/Resources/Music/Match.mp3")
    let musicAsset = AVURLAsset(url: musicURL)

    if let track = musicAsset.tracks(withMediaType: .audio).first {
        var filled = CMTime.zero
        let whole = CMTime(seconds: total, preferredTimescale: 600)

        while filled < whole {
            let piece = CMTimeMinimum(musicAsset.duration, CMTimeSubtract(whole, filled))
            try music.insertTimeRange(CMTimeRange(start: .zero, duration: piece), of: track, at: filled)
            filled = CMTimeAdd(filled, piece)
        }
    }

    let mix = AVMutableAudioMix()
    let musicParams = AVMutableAudioMixInputParameters(track: music)
    musicParams.setVolume(0.22, at: .zero)
    let speechParams = AVMutableAudioMixInputParameters(track: speech)
    speechParams.setVolume(1.0, at: .zero)
    mix.inputParameters = [musicParams, speechParams]

    let out = URL(fileURLWithPath: NSTemporaryDirectory() + "estalo-trailer-audio.m4a")
    try? FileManager.default.removeItem(at: out)

    guard let export = AVAssetExportSession(asset: composition, presetName: AVAssetExportPresetAppleM4A) else {
        throw NSError(domain: "trailer", code: 1)
    }

    export.outputURL = out
    export.outputFileType = .m4a
    export.audioMix = mix

    let wait = DispatchSemaphore(value: 0)
    export.exportAsynchronously { wait.signal() }
    wait.wait()

    if export.status != .completed {
        throw export.error ?? NSError(domain: "trailer", code: 2)
    }

    return out
}

let audioURL = try buildAudio()
print("sound: ready")

// --- writing the file ------------------------------------------------------
try? FileManager.default.removeItem(at: outURL)
let writer = try AVAssetWriter(outputURL: outURL, fileType: .mp4)

let videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: [
    AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: W, AVVideoHeightKey: H,
    AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: 10_000_000],
])
videoInput.expectsMediaDataInRealTime = false

let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: videoInput, sourcePixelBufferAttributes: [
    kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
    kCVPixelBufferWidthKey as String: W, kCVPixelBufferHeightKey as String: H,
])

let audioInput = AVAssetWriterInput(mediaType: .audio, outputSettings: [
    AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 48000, AVNumberOfChannelsKey: 2,
    AVEncoderBitRateKey: 192_000,
])
audioInput.expectsMediaDataInRealTime = false

writer.add(videoInput)
writer.add(audioInput)
writer.startWriting()
writer.startSession(atSourceTime: .zero)

let space = CGColorSpace(name: CGColorSpace.sRGB)!
var framesWritten = 0
let group = DispatchGroup()

group.enter()
videoInput.requestMediaDataWhenReady(on: DispatchQueue(label: "video")) {
    while videoInput.isReadyForMoreMediaData {
        let frame = framesWritten

        if frame >= frameCount {
            videoInput.markAsFinished()
            group.leave()
            return
        }

        var buffer: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &buffer)
        guard let pixels = buffer else { return }

        CVPixelBufferLockBaseAddress(pixels, [])
        let ctx = CGContext(data: CVPixelBufferGetBaseAddress(pixels), width: W, height: H,
                            bitsPerComponent: 8, bytesPerRow: CVPixelBufferGetBytesPerRow(pixels),
                            space: space,
                            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
        ctx.fill(CGRect(x: 0, y: 0, width: CGFloat(W), height: CGFloat(H)), CGColor(gray: 0, alpha: 1))

        // Which scene this frame belongs to, and how far into it.
        var t = Double(frame) / Double(FPS)

        for scene in scenes {
            if t < scene.length {
                scene.draw(ctx, t)
                break
            }

            t -= scene.length
        }

        CVPixelBufferUnlockBaseAddress(pixels, [])
        adaptor.append(pixels, withPresentationTime: CMTime(value: CMTimeValue(frame), timescale: FPS))
        framesWritten += 1

        if framesWritten % 150 == 0 {
            print("  \(framesWritten) / \(frameCount)")
        }
    }
}

group.enter()
// One asset for both: a reader refuses an output made from a different instance of the same file.
let audioAsset = AVURLAsset(url: audioURL)
let reader = try AVAssetReader(asset: audioAsset)
let pcm = AVAssetReaderTrackOutput(track: audioAsset.tracks(withMediaType: .audio)[0],
                                   outputSettings: [AVFormatIDKey: kAudioFormatLinearPCM,
                                                    AVLinearPCMBitDepthKey: 16,
                                                    AVLinearPCMIsFloatKey: false,
                                                    AVLinearPCMIsBigEndianKey: false,
                                                    AVLinearPCMIsNonInterleaved: false])
reader.add(pcm)
reader.startReading()

audioInput.requestMediaDataWhenReady(on: DispatchQueue(label: "audio")) {
    while audioInput.isReadyForMoreMediaData {
        if let sample = pcm.copyNextSampleBuffer() {
            audioInput.append(sample)
        } else {
            audioInput.markAsFinished()
            group.leave()
            return
        }
    }
}

group.wait()
let done = DispatchSemaphore(value: 0)
writer.finishWriting { done.signal() }
done.wait()

if writer.status == .completed {
    let size = (try? FileManager.default.attributesOfItem(atPath: outURL.path)[.size] as? Int) ?? 0
    print(String(format: "written: %@ (%.1f MB)", outURL.path, Double(size ?? 0) / 1_048_576))
} else {
    print("failed: \(writer.error?.localizedDescription ?? "unknown")")
    exit(1)
}
