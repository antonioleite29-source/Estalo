#!/usr/bin/env bash
# Records the narration with the voice that comes with macOS: Luciana, Brazilian Portuguese.
# One file per line, so the renderer can time each scene to the line that plays over it.
#
#   ./Tools/Video/narrate.sh
#
# These are the placeholder. A real recording named v1.mp3 (see voice/lines.txt) dropped straight into
# Tools/Video/voice is used instead of the file of the same name here.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/voice/mac"
mkdir -p "$OUT"

line() {
    say -v Luciana -r 165 -o "$OUT/$1.aiff" "$2"
    afconvert -f WAVE -d LEI16@48000 -c 1 "$OUT/$1.aiff" "$OUT/$1.wav"
    rm -f "$OUT/$1.aiff"
    echo "$1  $(afinfo "$OUT/$1.wav" | awk '/estimated duration/{printf "%.1fs", $3}')"
}

line v1 "Estalo é um duelo de matemática em tempo real. Duas pessoas, a mesma pergunta, ao mesmo tempo."
line v2 "Quem acertar primeiro marca o ponto."
line v3 "E tem uma regra que muda tudo: errou, o adversário ganha quatro segundos para responder sozinho."
line v4 "Fora das partidas, uma trilha de vinte lições, da adição às equações. Cada lição ensina primeiro e cobra depois."
line v5 "Tudo fica salvo: pontuação, histórico e o ranking de todo mundo que joga."
line v6 "Estalo. Matemática fica melhor quando é uma disputa com os amigos."
line v7 "Quem abre o jogo pela primeira vez aprende jogando: sete perguntas definem o seu nível, de um a sete."
line v8 "Dá para jogar em dupla também: dois contra dois, revezando quem responde a cada rodada."
