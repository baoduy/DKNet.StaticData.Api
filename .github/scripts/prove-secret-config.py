#!/usr/bin/env python3
"""Prove the rendered Secret env key reaches the unchanged app and a live database.

This checks the rendered Kubernetes contract and the .NET consumer locally.
It does not deploy Kubernetes or authenticate a cloud secret synchronizer.
"""
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import urllib.error
import urllib.request
import yaml


def run(*args, **kwargs):
    return subprocess.run(args, check=True, capture_output=True, text=True, **kwargs).stdout.strip()


root = Path(__file__).resolve().parents[2]
os.chdir(root)
rendered = list(yaml.safe_load_all(run("helm", "template", "proof", "helm/dknet-staticdata")))
resources = [item for item in rendered if item]
deployment = next(item for item in resources if item["kind"] == "Deployment")
config = next(item for item in resources if item["kind"] == "ConfigMap")["data"]
container = deployment["spec"]["template"]["spec"]["containers"][0]
assert {"secretRef": {"name": "staticdata-database"}} in container["envFrom"]
assert "ConnectionStrings__AppDb" not in config
assert not any(item["kind"] in ("Secret", "SecretProviderClass", "HTTPRoute") for item in resources)
assert container["livenessProbe"]["httpGet"]["path"] == "/healthz"
assert container["readinessProbe"]["httpGet"]["path"] == "/healthz"
password = secrets.token_hex(24)
name = "staticdata-secret-proof-" + secrets.token_hex(6)
app = None
try:
    run("docker", "run", "--detach", "--name", name, "--publish", "127.0.0.1::5432",
        "--env", "POSTGRES_PASSWORD=" + password, "postgres:16-alpine")
    for attempt in range(60):
        ready = subprocess.run(["docker", "exec", name, "pg_isready", "-U", "postgres"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if ready.returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError("Proof database did not become ready")
    port = json.loads(run("docker", "inspect", name))[0]["NetworkSettings"]["Ports"]["5432/tcp"][0]["HostPort"]
    # This is the data key of the operator-managed Kubernetes Secret; only ephemeral test content is generated.
    secret_data = {"ConnectionStrings__AppDb":
                   f"Host=127.0.0.1;Port={port};Database=postgres;Username=postgres;Password={password}"}
    env = os.environ.copy()
    env.update(config)
    env.update(secret_data)
    # Use an ephemeral local HTTP port for the process, just as the pod uses 8080.
    import socket
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        app_port = listener.getsockname()[1]
    env["ASPNETCORE_URLS"] = f"http://127.0.0.1:{app_port}"
    binary = root / "ApiEndpoints/DKNet.StaticData.Api/bin/Release/net10.0/DKNet.StaticData.Api.dll"
    app = subprocess.Popen(["dotnet", str(binary)], cwd=binary.parent, env=env,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for attempt in range(60):
        if app.poll() is not None:
            raise RuntimeError("Application exited before its health probe passed")
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{app_port}/healthz", timeout=2) as response:
                assert json.load(response) == {"status": "Healthy"}
            break
        except (urllib.error.URLError, TimeoutError):
            time.sleep(1)
    else:
        raise RuntimeError("Health probe did not become healthy")
    tables = run("docker", "exec", name, "psql", "-U", "postgres", "-tAc",
                 "SELECT count(*) FROM information_schema.tables WHERE table_name = 'CoreDbContext'")
    assert tables == "1", "Application startup did not migrate the database"
    print("Secret proof PASS: rendered envFrom -> ConnectionStrings__AppDb -> startup migration -> /healthz Healthy")
finally:
    if app is not None and app.poll() is None:
        app.terminate()
        try:
            app.wait(timeout=10)
        except subprocess.TimeoutExpired:
            app.kill()
            app.wait()
    subprocess.run(["docker", "rm", "--force", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
