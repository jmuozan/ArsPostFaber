#!/bin/bash

# Simple script to test if opening a browser works

echo "Testing browser opening on macOS..."

# Try the open command
echo "Testing with 'open' command..."
open "http://localhost:8000" 2>&1
echo "Exit code: $?"

# Try creating a Python HTTP server
echo "Testing Python HTTP server..."
TEMP_DIR=$(mktemp -d)
echo "Created temp directory: $TEMP_DIR"

# Create a simple Python HTTP server script
cat > "$TEMP_DIR/server.py" << EOF
import http.server
import socketserver

PORT = 8000

class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-type', 'text/html')
        self.end_headers()
        self.wfile.write(b"<html><body><h1>Test Server</h1><p>This is a test server.</p></body></html>")

print(f"Starting server at http://localhost:{PORT}")
with socketserver.TCPServer(("", PORT), Handler) as httpd:
    print("Server started")
    httpd.serve_forever()
EOF

# Run the server in the background
echo "Starting Python HTTP server..."
cd "$TEMP_DIR"
python3 "$TEMP_DIR/server.py" &
SERVER_PID=$!

# Wait a second for the server to start
sleep 1

# Try opening the browser
echo "Attempting to open browser..."
open "http://localhost:8000"

# Wait for user input
echo "Press Enter to exit and kill the server..."
read

# Kill the server
echo "Killing server process..."
kill $SERVER_PID

echo "Test complete"