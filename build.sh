#!/bin/bash

UNITY_PATH="/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="/Users/einar/Projects/ArenaURP"

# Mandatory Platform Argument
PLATFORM=$1

if [[ "$PLATFORM" != "linux" && "$PLATFORM" != "mac" && "$PLATFORM" != "windows" && "$PLATFORM" != "all" ]]; then
    echo "Usage: ./build.sh [linux|mac|windows|all] [--development] [--profiler]"
    exit 1
fi

# Collect optional flags
EXTRA_ARGS=""
for arg in "$@"; do
  if [ "$arg" == "--development" ]; then EXTRA_ARGS="$EXTRA_ARGS -development"; fi
  if [ "$arg" == "--profiler" ]; then EXTRA_ARGS="$EXTRA_ARGS -profiler"; fi
done

# Define the build function to avoid repetition
run_build() {
    local target=$1
    echo "🚀 Building for platform: $target with extras: $EXTRA_ARGS"
    $UNITY_PATH -quit -batchmode -projectPath "$PROJECT_PATH" \
      -executeMethod BuildScripts.BuildProject \
      -logFile "build_$target.log" \
      -buildPlatform "$target" $EXTRA_ARGS
    
    if [ $? -eq 0 ]; then
        echo "✅ $target build successful."
    else
        echo "❌ $target build failed. Check build_$target.log"
        exit 1
    fi
}

if [ "$PLATFORM" == "all" ]; then
    run_build "linux"
    run_build "windows"
    run_build "mac"
    echo "🎉 All builds completed successfully!"
else
    run_build "$PLATFORM"
fi