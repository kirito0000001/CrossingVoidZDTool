import json
import os
import runpy


job_path = globals().get("ZD_REMOTE_JOB_PATH", "")
if not job_path or not os.path.isfile(job_path):
    raise RuntimeError("remote Unreal job file does not exist")

with open(job_path, "r", encoding="utf-8") as source:
    job = json.load(source)
if int(job.get("protocolVersion", 0)) != 1:
    raise RuntimeError("unsupported remote Unreal job protocol")

script_path = os.path.abspath(job.get("scriptPath", ""))
if not os.path.isfile(script_path):
    raise RuntimeError("remote Unreal task script does not exist: " + script_path)

environment = dict(job.get("environment", {}))
previous_environment = {key: os.environ.get(key) for key in environment}
try:
    for key, value in environment.items():
        os.environ[str(key)] = str(value)
    runpy.run_path(script_path, run_name="__main__")
finally:
    for key, value in previous_environment.items():
        if value is None:
            os.environ.pop(key, None)
        else:
            os.environ[key] = value
