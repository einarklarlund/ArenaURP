#!/bin/bash

# Configuration

REMOTE_USER="root"
REMOTE_HOST="167.99.51.139"
PORT=7777
REMOTE_BUILD_PATH="/home/game-server"
REMOTE_EXECUTABLE_PATH="$REMOTE_BUILD_PATH/linux_server"
REMOTE_EXECUTABLE="$REMOTE_EXECUTABLE_PATH/arena-server.x86_64"
LOCAL_BUILD_PATH="$HOME/Projects/ArenaURP/builds/linux_server"
PORT=7777

echo "--- Starting Deployment ---"

# 1. Transfer files to the server (Syncing)
# This ensures only changed files are uploaded
echo "Syncing files to $REMOTE_HOST..."
rsync -avz --delete "$LOCAL_BUILD_PATH" "$REMOTE_USER@$REMOTE_HOST:$REMOTE_BUILD_PATH"

# 2. Remote Execution
echo "Starting the server remotely..."
ssh "$REMOTE_USER@$REMOTE_HOST" << EOF

    # 1. Find and kill any process using the specified port
    # -k: kill, -n: namespace (udp), -s: silent

    echo "Terminating existing process on port $PORT..."
    fuser -k ${PORT}/udp || echo "No existing process found on port $PORT."
    

    # 2. Clear old logs

    cd "$REMOTE_EXECUTABLE_PATH"
    echo "Clearing old log files..."
    > server.log
    > stdout.log


    # 3. Ensure the file has execution permissions

    chmod +x "$REMOTE_EXECUTABLE"
    

    # 4. Run the server in the background (using nohup so it stays up after you disconnect)
    # -batchmode and -nographics are standard for headless Unity servers

    echo "Starting server..."
    nohup $REMOTE_EXECUTABLE -batchmode -nographics -logfile server.log > stdout.log 2>&1 < /dev/null &

    # 5. Health Check

    echo "Performing health check on port $PORT..."
    MAX_RETRIES=10
    COUNT=0
    SUCCESS=false

    while [ \$COUNT -lt \$MAX_RETRIES ]; do
        if ss -ulu | grep -q ":$PORT"; then
            echo "SUCCESS: Server is listening on UDP port $PORT!"
            SUCCESS=true
            break
        fi
        
        echo "Waiting for server to bind to port (Attempt \$((COUNT+1))/\$MAX_RETRIES)..."
        sleep 2
        COUNT=\$((COUNT+1))
    done

    if [ "\$SUCCESS" = false ]; then
        echo "ERROR: Health check failed. Printing last 20 lines of server.log:"
        tail -n 20 server.log
        exit 1
    fi
    
    exit 0
    echo "exited"
EOF

if [ $? -eq 0 ]; then
    echo "--- Deployment Successful ---"
else
    echo "--- Deployment Failed ---"
    exit 1
fi