#!/bin/zsh
# Headless verification: compile, configure the project, rebuild generated content, run all tests.
# Usage: ./run_checks.sh [setup|tests|all|build|mac]
#   mac builds the desktop player used by Tools/coop_check.py
set -u
cd "$(dirname "$0")"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity}"
MODE="${1:-all}"
mkdir -p Logs

if [[ "$MODE" == "setup" || "$MODE" == "all" ]]; then
  "$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod SmallTown.EditorTools.ProjectSetup.ConfigureProject -logFile Logs/setup-configure.log
  echo "configure exit=$?"
  "$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod SmallTown.EditorTools.ContentBuilder.BuildAll -logFile Logs/setup-content.log
  echo "content exit=$?"
fi

if [[ "$MODE" == "tests" || "$MODE" == "all" ]]; then
  "$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile Logs/editmode.log
  echo "editmode exit=$?"
  "$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Logs/playmode-results.xml" -logFile Logs/playmode.log
  echo "playmode exit=$?"
fi

if [[ "$MODE" == "build" ]]; then
  "$UNITY" -batchmode -quit -buildTarget Android -projectPath "$PWD" -executeMethod SmallTown.EditorTools.BuildScript.BuildAndroidDevelopment -logFile Logs/build-android.log
  echo "android build exit=$?"
fi

if [[ "$MODE" == "mac" ]]; then
  "$UNITY" -batchmode -quit -buildTarget StandaloneOSX -projectPath "$PWD" -executeMethod SmallTown.EditorTools.BuildScript.BuildMacDevelopment -logFile Logs/build-mac.log
  echo "mac build exit=$?"
fi
