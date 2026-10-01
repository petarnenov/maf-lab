"""The A2A Inspector's own app, opened on the lab: the agent card's address filled in, and a fresh partner token.

Upstream serves index.html with an empty card URL and no auth. This module imports that app unchanged, replaces only
its `/` route with the same page plus one script, and adds `/lab/token`, which asks the lab for a partner token with
the dev credentials compose passes in. The token is minted per request, so a page opened hours later still works.
"""

import json
import os
import sys
import urllib.request

sys.path.insert(0, '/app/backend')

from app import app, templates  # noqa: E402  (upstream's FastAPI app and its template directory)
from fastapi import Request  # noqa: E402
from fastapi.responses import HTMLResponse, JSONResponse, Response  # noqa: E402

LAB = os.environ.get('LAB_URL', 'http://localhost:7171')
# Each agent has its own token endpoint and audience; a token for one is refused by the other.
AGENTS = {
    'assistant': {
        'card': f'{LAB}/.well-known/agent-card.json',
        'token': f'{LAB}/a2a/token',
        'client_id': os.environ.get('LAB_A2A_CLIENT_ID', 'acme-portal'),
        'secret': os.environ.get('LAB_A2A_CLIENT_SECRET', ''),
    },
    'compliance': {
        'card': f'{LAB}/compliance/.well-known/agent-card.json',
        'token': f'{LAB}/compliance/a2a/token',
        'client_id': os.environ.get('LAB_COMPLIANCE_CLIENT_ID', 'maf-lab-assistant'),
        'secret': os.environ.get('LAB_COMPLIANCE_CLIENT_SECRET', ''),
    },
}

app.router.routes = [r for r in app.router.routes if getattr(r, 'path', None) != '/']


@app.get('/', response_class=HTMLResponse)
async def index(request: Request) -> Response:
    page = templates.TemplateResponse('index.html', {'request': request})
    html = page.body.decode('utf-8').replace('</body>', '<script src="/lab/defaults.js"></script></body>', 1)
    return HTMLResponse(html)


@app.get('/lab/token')
def token(agent: str = 'assistant') -> Response:
    target = AGENTS.get(agent)
    if target is None or not target['secret']:
        return JSONResponse({'error': 'unknown agent or no credentials'}, status_code=404)
    body = json.dumps({'clientId': target['client_id'], 'clientSecret': target['secret']}).encode()
    request = urllib.request.Request(target['token'], data=body, headers={'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=5) as reply:
            access = json.load(reply)['accessToken']
    except Exception as ex:  # the lab is down or refused: the page falls back to typing it by hand
        return JSONResponse({'error': type(ex).__name__}, status_code=502)
    return JSONResponse({'accessToken': access}, headers={'Cache-Control': 'no-store'})


@app.get('/lab/defaults.js')
def defaults() -> Response:
    cards = json.dumps({name: a['card'] for name, a in AGENTS.items()})
    script = f"""
(() => {{
  const cards = {cards};
  const url = document.getElementById('agent-card-url');
  const auth = document.getElementById('auth-type');
  const agentOf = (value) => (value.includes('/compliance/') ? 'compliance' : 'assistant');
  async function fill() {{
    const reply = await fetch('/lab/token?agent=' + agentOf(url.value), {{cache: 'no-store'}});
    if (!reply.ok) return;
    const {{accessToken}} = await reply.json();
    if (auth && auth.value !== 'bearer') {{
      auth.value = 'bearer';
      auth.dispatchEvent(new Event('change'));
    }}
    const field = document.getElementById('bearer-token');
    if (field) field.value = accessToken;
  }}
  if (url && !url.value) url.value = cards.assistant;
  if (url) url.addEventListener('change', fill);
  fill();
}})();
"""
    return Response(script, media_type='application/javascript', headers={'Cache-Control': 'no-store'})
