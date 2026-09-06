import argparse
import json
import os
import runpy
import sys
import time


def _normalized(path):
    return os.path.normcase(os.path.abspath(path or "").rstrip("\\/"))


def _load_job(path):
    with open(path, "r", encoding="utf-8") as source:
        job = json.load(source)
    if int(job.get("protocolVersion", 0)) != 1:
        raise RuntimeError("unsupported remote Unreal job protocol")
    return job


def _find_matching_node(remote, project_path, engine_root, timeout_seconds):
    expected_project_root = _normalized(os.path.dirname(project_path))
    expected_engine_root = _normalized(engine_root)
    expected_engine_roots = {expected_engine_root, _normalized(os.path.join(engine_root, "Engine"))}
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        for node in remote.remote_nodes:
            node_engine_root = _normalized(node.get("engine_root"))
            if (_normalized(node.get("project_root")) == expected_project_root and
                    node_engine_root in expected_engine_roots):
                return node
        time.sleep(0.2)
    return None


def _print_remote_output(result):
    for item in result.get("output", []):
        output = item.get("output", "")
        if output:
            print(output, end="" if output.endswith("\n") else "\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--job", required=True)
    parser.add_argument("--timeout", type=float, default=6.0)
    args = parser.parse_args()
    job_path = os.path.abspath(args.job)
    job = _load_job(job_path)
    engine_root = os.path.abspath(job["engineRoot"])
    remote_module_candidates = [
        os.path.join(engine_root, "Engine", "Plugins", "Experimental", "PythonScriptPlugin", "Content", "Python"),
        os.path.join(engine_root, "Plugins", "Experimental", "PythonScriptPlugin", "Content", "Python"),
    ]
    remote_module_path = next((path for path in remote_module_candidates if os.path.isfile(os.path.join(path, "remote_execution.py"))), None)
    if remote_module_path is None:
        raise RuntimeError("Unreal Python remote_execution.py was not found under engine root: " + engine_root)
    sys.path.insert(0, remote_module_path)
    try:
        import remote_execution
    finally:
        sys.path.remove(remote_module_path)

    remote = remote_execution.RemoteExecution()
    remote.start()
    try:
        node = _find_matching_node(
            remote,
            job["projectPath"],
            engine_root,
            args.timeout)
        if node is None:
            raise RuntimeError(
                "No running Unreal Editor with Python Remote Execution enabled matches "
                "the selected project and engine. The target Editor may also be busy running "
                "another remote Python task. Enable Project Settings > Plugins > Python > "
                "Enable Remote Execution, then wait for any current task to finish.")

        bootstrap_path = os.path.join(
            os.path.dirname(os.path.abspath(__file__)),
            "execute_remote_unreal_job.py")
        command = (
            "import runpy\n"
            "runpy.run_path({!r}, init_globals={{'ZD_REMOTE_JOB_PATH': {!r}}})"
        ).format(bootstrap_path, job_path)
        remote.open_command_connection(node["node_id"])
        result = remote.run_command(
            command,
            unattended=True,
            exec_mode=remote_execution.MODE_EXEC_FILE)
        _print_remote_output(result)
        if not result.get("success", False):
            raise RuntimeError(str(result.get("result", "remote Unreal command failed")))
        return 0
    finally:
        remote.stop()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print("Remote Unreal execution failed: {}".format(error), file=sys.stderr)
        raise SystemExit(1)
