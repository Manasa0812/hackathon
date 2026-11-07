#!/usr/bin/env python3
"""Translate Prometheus Alertmanager webhook payloads to Mattermost incoming posts."""
import json
import os
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.request import Request, urlopen

WEBHOOK_FILE = os.getenv("MM_WEBHOOK_FILE", "/run/secrets/mattermost-webhook")

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/alert":
            self.send_error(404)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(size))
            blocks = ["@resolveiq-investigator **ResolveIQ alert: " + payload.get("status", "unknown").upper() + "**"]
            for alert in payload.get("alerts", []):
                labels = alert.get("labels", {})
                annotations = alert.get("annotations", {})
                blocks.append(
                    f"**{annotations.get('summary', labels.get('alertname', 'Alert'))}**\n"
                    f"{annotations.get('description', '')}\n"
                    f"Service: `{labels.get('service', 'unknown')}` | Severity: `{labels.get('severity', 'unknown')}`"
                )
            hook = open(WEBHOOK_FILE, encoding="utf-8").read().strip()
            body = json.dumps({"text": "\n\n".join(blocks)}).encode()
            request = Request(hook, data=body, headers={"Content-Type": "application/json"}, method="POST")
            with urlopen(request, timeout=8) as response:
                response.read()
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"forwarded")
        except Exception as exc:
            print(f"alert forwarding failed: {type(exc).__name__}", flush=True)
            self.send_error(502)

    def log_message(self, fmt, *args):
        print(fmt % args, flush=True)

HTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
