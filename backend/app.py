from flask import Flask, request
import os, requests, uuid, time, logging
from azure.identity import EnvironmentCredential
from flask_cors import CORS
from dotenv import load_dotenv

load_dotenv()
logging.basicConfig(level=logging.INFO)

app = Flask(__name__)
CORS(app, resources={r"/*": {"origins": "*"}})

POOL_ENDPOINT = os.getenv("POOL_ENDPOINT")
if not POOL_ENDPOINT:
    raise RuntimeError("POOL_ENDPOINT env var ontbreekt")

SCOPE = "https://dynamicsessions.io/.default"

# Read Azure details from .env file
cred = EnvironmentCredential()

# Get Azure access token
def get_token():
    return cred.get_token(SCOPE).token

# post with retry function to prevent errors during container startup
def post_with_retry(url, json, headers, base_timeout=180, max_retries=8):
    delay = 1.0
    last = None
    for attempt in range(1, max_retries + 1):
        r = requests.post(url, json=json, headers=headers, timeout=base_timeout)
        logging.info("attempt=%s status=%s retry_after=%s", attempt, r.status_code, r.headers.get("Retry-After"))
        if r.status_code != 429:
            return r
        ra = r.headers.get("Retry-After")
        sleep_s = int(ra) if (ra and ra.isdigit()) else delay
        time.sleep(sleep_s)
        delay = min(delay * 1.8, 12)
        last = r
    return last if last is not None else r

@app.post("/run")
def run():
    try:
        body = request.get_json(force=True) or {}
        code = body.get("code")
        if not code:
            return {"error": "code ontbreekt in body"}, 400

        # Always generate unique session ID to force new container startup
        session_id = f"run-{uuid.uuid4().hex[:8]}"

        url = f"{POOL_ENDPOINT}/runner?identifier={session_id}"
        headers = {
            "Authorization": f"Bearer {get_token()}",
            "Content-Type": "application/json",
        }
        payload = {"code": code}

        r = post_with_retry(url, payload, headers)

        return (r.text, r.status_code, {"Content-Type": r.headers.get("content-type", "application/json")})

    except Exception as e:
        logging.exception("Fout in /run")
        return {"error": "Interne serverfout in Flask-app", "details": str(e)}, 500


@app.get("/health")
def health():
    return {"status": "ok"}

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=50505)
