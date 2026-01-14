#!/bin/bash

# CLI flags
no_cache=false

# Parse command-line flags
while getopts "c" flag; do
  case $flag in
    c) no_cache=true ;;
    *) exit 1 ;;
  esac
done

# Detect docker-compose command (try legacy first, then new)
if command -v docker-compose &> /dev/null; then
    DOCKER_COMPOSE="docker-compose"
else
    DOCKER_COMPOSE="docker compose"
fi

# For more verbose Docker build logs
# export BUILDKIT_PROGRESS=plain

# Build with no cache to ensure fresh build
echo "Building Docker Containers"
if [ "$no_cache" = true ]; then
    $DOCKER_COMPOSE build --no-cache
else
    $DOCKER_COMPOSE build
fi

# Run in foreground with all logs visible
echo "Starting containers..."
$DOCKER_COMPOSE up --no-build

