import argparse
import os
import re
import sys

from openai import OpenAI

DATA_DIR = "data"
RESPONSE_FILE = os.path.join(DATA_DIR, "response.txt")
PATTERN = r"```csharp(.*?)```"


def get_max_output_tokens():
    return int(os.environ.get("OPENAI_MAX_COMPLETION_TOKENS") or os.environ.get("OPENAI_MAX_TOKENS") or "1000")


def uses_max_completion_tokens(model):
    normalized = model.lower().replace(" ", "-")
    return normalized.startswith(("gpt-5", "o1", "o3", "o4"))


def create_chat_completion(client, model, message_log):
    params = {
        "model": model,
        "messages": message_log,
    }

    if uses_max_completion_tokens(model):
        params["max_completion_tokens"] = get_max_output_tokens()
    else:
        params["max_tokens"] = get_max_output_tokens()
        params["temperature"] = 0.7

    try:
        return client.chat.completions.create(**params)
    except Exception as exc:
        text = str(exc)
        if "max_tokens" in text and "max_completion_tokens" in text:
            params.pop("max_tokens", None)
            params["max_completion_tokens"] = get_max_output_tokens()
            return client.chat.completions.create(**params)
        if "temperature" in text:
            params.pop("temperature", None)
            return client.chat.completions.create(**params)
        raise


def request_response(client, message_log, model):
    try:
        response = create_chat_completion(client, model, message_log)
    except Exception as exc:
        print(f"[CodeGenerationService] OpenAI request failed: {exc}", file=sys.stderr, flush=True)
        return message_log, False

    content = response.choices[0].message.content or ""
    os.makedirs(DATA_DIR, exist_ok=True)
    with open(RESPONSE_FILE, "a", encoding="utf-8") as file:
        file.write("\n==========\n")
        file.write(content)

    matches = re.findall(PATTERN, content, re.DOTALL)
    generated = matches[0] if matches else "CODE-GENERATION ERROR"
    message_log.append({"role": "assistant", "content": generated})
    return message_log, True


def listen_for_messages(args, client):
    message_log = [{"role": "system", "content": ""}]

    while True:
        try:
            line = bytes(args.prompt, "utf-8") if args.prompt else sys.stdin.buffer.readline()
            if len(line) == 0 or line.isspace():
                continue

            args.prompt = ""
            prompt = f"{args.preprompt} {line.decode('utf-8').strip()} {args.prompt_suffix}"
            message_log.append({"role": "user", "content": prompt})
            message_log, ok = request_response(client, message_log, args.model)
            if ok:
                print(">" + message_log[-1]["content"], flush=True)
        except KeyboardInterrupt:
            break


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--preprompt", type=str, default="")
    parser.add_argument("--prompt_suffix", type=str, default="")
    parser.add_argument("--prompt", type=str, default="")
    parser.add_argument("--key", type=str, default="")
    parser.add_argument("--model", type=str, default="gpt-4o-mini")
    args = parser.parse_args()

    api_key = args.key or os.environ.get("OPENAI_API_KEY", "")
    if not api_key:
        print("[CodeGenerationService] Missing OpenAI API key. Set OPENAI_API_KEY.", file=sys.stderr, flush=True)

    listen_for_messages(args, OpenAI(api_key=api_key))
