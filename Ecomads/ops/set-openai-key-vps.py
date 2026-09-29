#!/usr/bin/env python3
import os
import sys
import tempfile


env_path = "/opt/ecomads/.env"
key = sys.stdin.read().rstrip("\r\n")

if not key or "\n" in key or "\r" in key:
    raise SystemExit("The API key is empty or contains a newline.")

with open(env_path, "r", encoding="utf-8") as source:
    lines = source.read().splitlines()

updated = []
replaced = False
for line in lines:
    if line.startswith("OPENAI_API_KEY="):
        updated.append(f"OPENAI_API_KEY={key}")
        replaced = True
    else:
        updated.append(line)

if not replaced:
    updated.append(f"OPENAI_API_KEY={key}")

fd, temp_path = tempfile.mkstemp(prefix=".env.", dir="/opt/ecomads", text=True)
try:
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as target:
        target.write("\n".join(updated) + "\n")
        target.flush()
        os.fsync(target.fileno())
    os.replace(temp_path, env_path)
finally:
    if os.path.exists(temp_path):
        os.unlink(temp_path)
