#!/bin/bash

# Path configurations
UNITY_PATH="/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="/Users/einar/Projects/ArenaURP"
BUILD_PATH="$PROJECT_PATH/builds/webgl"

# Parse flags
SKIP_BUILD=false
while getopts "s" opt; do
  case $opt in
    s) SKIP_BUILD=true ;;
    *) echo "Usage: $0 [-s (skip build)]"; exit 1 ;;
  esac
done

UNITY_WAS_RUNNING=false

if [ "$SKIP_BUILD" = false ]; then
    if ps aux | grep -v grep | grep "$UNITY_PATH" | grep -q "$PROJECT_PATH"; then
        echo "--- Unity Editor detected. Closing instance to allow build ---"
        UNITY_WAS_RUNNING=true
        pkill -f "$PROJECT_PATH"
        sleep 2 
    fi

    echo "--- Building WebGL Development Version ---"
    $UNITY_PATH -quit -batchmode \
        -projectPath "$PROJECT_PATH" \
        -executeMethod BuildScripts.BuildWebGLDevelopment \
        -logfile -
else
    echo "--- Skip flag detected: Skipping Unity build step ---"
fi

# --- Starting Local Web Server ---
echo "--- Starting Local Web Server ---"
cd "$BUILD_PATH"
npx http-server -p 8080 &
WEB_PID=$!

echo "Local environment is live!"
echo "Web server: http://localhost:8080"
    
if [ "$UNITY_WAS_RUNNING" = true ]; then
    echo "--- Reopening Unity Editor ---"
    nohup "$UNITY_PATH" -projectPath "$PROJECT_PATH" > /dev/null 2>&1 &
fi

cleanup() {
    echo ""
    echo "--- Shutting down web server ---"
    kill $WEB_PID 2>/dev/null
}

trap cleanup EXIT
wait