import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1])
STUB = sys.argv[2]
BASE = f'http://127.0.0.1:{PORT}'
SIZE = os.path.getsize(STUB)

CONFIG = {
    'gateway': '127.0.0.1:20042',
    'login': '127.0.0.1:20040',
    'arguments': ['Portrait:UploadUrl=http://127.0.0.1/image'],
    'clientManifestUrl': f'{BASE}/client/manifest.xml',
    'clientBaseUrl': f'{BASE}/client',
    'purchaseCertificate': None,
}

MANIFEST = f'<?xml version="1.0"?><Manifest><Folder name=""><File name="FreeRealms.exe" size="{SIZE}" /></Folder></Manifest>'


class Handler(BaseHTTPRequestHandler):
    def send(self, status, body, content_type):
        data = body if isinstance(body, bytes) else body.encode()
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path == '/website/plugin/config':
            self.send(200, json.dumps(CONFIG), 'application/json')
        elif path == '/client/manifest.xml':
            self.send(200, MANIFEST, 'application/xml')
        elif path == '/client/FreeRealms.exe':
            self.send(200, open(STUB, 'rb').read(), 'application/octet-stream')
        else:
            self.send(404, '', 'text/plain')

    def log_message(self, fmt, *args):
        sys.stderr.write('mock_site: ' + (fmt % args) + '\n')


ThreadingHTTPServer(('127.0.0.1', PORT), Handler).serve_forever()
