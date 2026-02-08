#!/bin/bash
# Post-start script: runs after VS Code sets up git config

# Start docker daemon (original version - /tmp already mounted via runArgs)
/usr/local/share/docker-init.sh

echo "✓ Post-start configuration complete"
