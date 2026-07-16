#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
WING Session Extractor

Usage:
  ./wing-extract.sh INPUT_DIR OUTPUT_DIR

Example:
  ./wing-extract.sh \
    "/Users/timbautz/Music/tow/probe210626/rawsd1" \
    "/Users/timbautz/Music/WING_JOINED"

The script:
- finds all */00000001.WAV files below INPUT_DIR
- sorts the fixed-width hexadecimal session folder names chronologically
- extracts channels 1..16 as mono 48 kHz / 32-bit PCM
- concatenates all sessions per channel
- creates CH01.wav ... CH16.wav
EOF
}

if [[ $# -ne 2 ]]; then
  usage
  exit 1
fi

INPUT_DIR="${1%/}"
OUTPUT_DIR="${2%/}"
CHANNELS=16
SOURCE_NAME="00000001.WAV"

command -v ffmpeg >/dev/null 2>&1 || {
  echo "Fehler: ffmpeg ist nicht installiert." >&2
  echo "Installation: brew install ffmpeg" >&2
  exit 2
}

[[ -d "$INPUT_DIR" ]] || {
  echo "Fehler: Eingabeordner nicht gefunden: $INPUT_DIR" >&2
  exit 2
}

mkdir -p "$OUTPUT_DIR"

WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/wing-extract.XXXXXX")"
trap 'rm -rf "$WORK_DIR"' EXIT

SESSION_LIST="$WORK_DIR/sessions.txt"

# Bei achtstelligen Hex-IDs entspricht lexikographische Sortierung der numerischen Reihenfolge.
find "$INPUT_DIR" -type f -name "$SOURCE_NAME" -print0 \
  | while IFS= read -r -d '' file; do
      session="$(basename "$(dirname "$file")")"
      printf '%s\t%s\n' "$session" "$file"
    done \
  | LC_ALL=C sort -t $'\t' -k1,1 \
  > "$SESSION_LIST"

SESSION_COUNT="$(wc -l < "$SESSION_LIST" | tr -d ' ')"

if [[ "$SESSION_COUNT" -eq 0 ]]; then
  echo "Fehler: Keine $SOURCE_NAME unter $INPUT_DIR gefunden." >&2
  exit 3
fi

echo "Gefunden: $SESSION_COUNT Sessions"
cut -f1 "$SESSION_LIST" | sed 's/^/  /'
echo

for channel_index in $(seq 0 $((CHANNELS - 1))); do
  channel_number=$((channel_index + 1))
  channel_name="$(printf 'CH%02d' "$channel_number")"
  channel_dir="$WORK_DIR/$channel_name"
  concat_list="$channel_dir/concat.txt"
  mkdir -p "$channel_dir"
  : > "$concat_list"

  echo "Extrahiere $channel_name ..."

  part=0
  while IFS=$'\t' read -r session source_file; do
    part=$((part + 1))
    part_file="$channel_dir/$(printf '%04d_%s.wav' "$part" "$session")"

    ffmpeg -hide_banner -loglevel error -y \
      -i "$source_file" \
      -af "pan=mono|c0=c${channel_index}" \
      -ar 48000 \
      -c:a pcm_s32le \
      "$part_file"

    # ffmpeg concat demuxer escaping for single quotes
    escaped="${part_file//\'/\'\\\'\'}"
    printf "file '%s'\n" "$escaped" >> "$concat_list"
  done < "$SESSION_LIST"

  output_file="$OUTPUT_DIR/$channel_name.wav"

  ffmpeg -hide_banner -loglevel error -y \
    -f concat -safe 0 \
    -i "$concat_list" \
    -c copy \
    "$output_file"

  echo "  -> $output_file"
done

echo
echo "Fertig: $OUTPUT_DIR"
