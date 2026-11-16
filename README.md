# Oxenfree 한글패치 — by.김쥬크

Steam판 **Oxenfree** 최신 버전용 한글패치입니다. 

## 설치
1. [Releases](../../releases)에서 `Oxenfree_KR_v1.0.exe`를 받습니다.
2. 실행하면 Steam 게임 폴더를 자동으로 찾습니다 (못 찾으면 **[폴더]**로 직접 고르세요).
3. **[설치]**를 누르고, 끝나면 **[실행]**으로 게임을 시작합니다.

- 원본 `Assembly-CSharp.dll`은 `Assembly-CSharp.dll.ko_backup`으로 백업되고, **[복원]**으로 언제든 되돌릴 수 있습니다.
- 스팀 업데이트로 영어로 돌아가면 **[설치]**를 다시 누르면 됩니다.
- 설치 파일에는 게임 원본 데이터가 들어 있지 않습니다. 정품 게임 파일에 바뀐 부분만 적용합니다.
- 서명 없는 프로그램이라 Windows 경고가 뜨면 [추가 정보] → [실행]을 누르세요.
- 필요 환경: Windows 10/11 (.NET Framework 4.x 기본 포함)

## 원리
- 최신 Steam판에는 넷플릭스판용 **공식 한국어 번역**(대사, 선택지·메뉴, 지도 이미지)과 한글 폰트(Cute Font)가 이미 들어 있지만, Steam판 언어 목록에서 빠져 있어 고를 수 없습니다.
- 패치는 번역을 새로 넣지 않고 그 한국어를 켭니다.
  - `Dialog.supportedLanguages`의 러시아어 칸을 한국어로 교체
  - 언어 선택 메뉴의 해당 칸 글자를 "한국어"로 표시
  - 게임 언어 설정(레지스트리)을 한국어로 지정
  - 화면 아래 자막의 검은 바탕을 없애고 글씨에 테두리를 넣어 글씨만 보이게 함
- 코드를 위치가 아니라 이름으로 찾아 고치므로, 게임이 업데이트되어도 다시 적용할 수 있습니다.
- 패치 후에는 러시아어를 고를 수 없습니다.

## 설치기 (김쥬크 설치기)
- 주크박스 모양 창. 버튼: 설치 / 복원 / 폴더 / 실행 / 닫기, LED로 상태 표시
- 배경음과 간판 음표: 곡 박자에 맞춰 음표가 흔들림. 오른쪽 위 스피커 아이콘 또는 M 키로 켜고 끔
- Mono.Cecil을 exe 안에 넣어 파일 하나로 배포

## 저장소 구성
- `installer/` 설치기 소스 (C# WinForms, 주크박스 창·패치 로직·좌표)
- `skin/` 주크박스 스킨 원본 그림과 배경 제거·버튼 정리 스크립트 (Layout.cs 생성)
- `music/` 박자 지도 생성 스크립트, 배경음 박자 데이터 (곡 파일은 포함하지 않음)
- `lib/` .NET Framework용으로 고친 Mono.Cecil
- `tools/` 빌드 스크립트, Mono.Cecil 수정·검사 도구
- `notes/` 작업 노트

## 감사
- 한국어 번역·한글 폰트: 게임에 들어 있는 공식 리소스 (Cute Font by TypoDesign Lab. Inc — SIL Open Font License 1.1)
- 배경음: "Psychedelic Blues Rock Instrumental" by nickpanek ([Pixabay](https://pixabay.com/)) — Pixabay Content License, 설치기 안에만 포함
- [Mono.Cecil](https://github.com/jbevain/cecil) by Jb Evain — MIT License
- 원작: Night School Studio
