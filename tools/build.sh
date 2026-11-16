#!/bin/sh
# 설치기 빌드 (mono의 mcs, .NET Framework 4.x 대상)
# 준비: music/bgm_psych.mp3 (Pixabay에서 받은 곡을 128kbps로 변환, 저장소에는 포함하지 않음)
#   ffmpeg -i nickpanek-psychedelic-blues-rock-instrumental-281979.mp3 -b:a 128k -ar 44100 music/bgm_psych.mp3
set -e
cd "$(dirname "$0")/.."
mkdir -p build
cp lib/Mono.Cecil.dll installer/Mono.Cecil.dll
cd installer
mcs -sdk:4.5 -codepage:utf8 -target:winexe -optimize+ -win32icon:juke.ico \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:Mono.Cecil.dll \
  -resource:skin.png,skin.png -resource:Mono.Cecil.dll,Mono.Cecil.dll \
  -resource:../music/bgm_psych.mp3,bgm.mp3 -resource:../music/bgm_psych.env,bgm.env -resource:../music/bgm_psych.beat,bgm.beat \
  MainForm.cs Patcher.cs Layout.cs -out:../build/Oxenfree_KR_v1.0.exe
rm -f Mono.Cecil.dll
echo "build/Oxenfree_KR_v1.0.exe"
