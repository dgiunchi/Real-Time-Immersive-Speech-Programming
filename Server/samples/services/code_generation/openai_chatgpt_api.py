import json
import sys
import openai
import argparse
import re
import os
import time

debug = 0
start = -1
end   = -1

pattern = r'```csharp(.*?)```'

def request_response(message_log, model):
    if not openai.api_key:
        print("[CodeGenerationService] Missing OpenAI API key. Set OPENAI_API_KEY or config.credentials.openAI.key.", file=sys.stderr, flush=True)
        return message_log, False

    try:
        response = openai.ChatCompletion.create(model=model, messages=message_log, max_tokens=1000, temperature=0.7)
    except Exception as exc:
        print(f"[CodeGenerationService] OpenAI request failed: {exc}", file=sys.stderr, flush=True)
        return message_log, False
    
    with open(os.path.join("data", "response.txt"), "a") as file:
        file.write("\n==========\n")
        file.write(response.choices[0].message['content'])
    
    text_after_first_answer = response.choices[0].message['content']
    matches = re.findall(pattern, text_after_first_answer, re.DOTALL)
    
    response.choices[0].message['content'] = matches[0] if len(matches) > 0 else "CODE-GENERATION ERROR"
    
    message_log.append(response.choices[0].message)
    return message_log, True


def listen_for_messages(args):
    message_log = [{"role": "system", "content": ""}]

    while True:
        try:
            line = bytes(args.prompt, 'utf-8') if args.prompt != "" else sys.stdin.buffer.readline()
            if len(line) == 0 or line.isspace():
                continue
                
            args.prompt = "" 
            message_log.append(
                {"role": "user", "content": args.preprompt + " " + line.decode("utf-8").strip() + " " + args.prompt_suffix}
            )
            if(debug):
                start = time.time()
                
            message_log, ok = request_response(message_log, args.model)
            if ok:
                print(">" + message_log[-1]["content"], flush=True)
            
            if(debug):
                end = time.time()
                print("time required:", end-start )
            
            
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

    openai.api_key = args.key or os.environ.get("OPENAI_API_KEY", "")

    listen_for_messages(args)
