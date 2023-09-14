import json
import sys
from gpt4all import GPT4All
import argparse


def request_response(log):
    # NON real time
    response = model.generate(prompt=log[-1]['content'], max_tokens=1000, temp=0.7)
    log.append({"role": "aimodel", "content": response})    
    print(">" + log[-1]["content"])
    
    # real time
    """
    tokens = []
    for token in model.generate(log[-1]['content'], max_tokens=1000, temp=0.7, streaming=True):
        tokens.append(token)
        if len(tokens) % 10 == 0:
            log.append({"role": "aimodel", "content": "".join(tokens)})    
            print(">" + log[-1]["content"])
            tokens = [] # cleanup
    """

def listen_for_messages(args, model):
    log = [
        {"role": "system", "content": args.preprompt}
    ]
    
    if args.cli : print("Ready...")
    
    while True:
        try:
            line = sys.stdin.buffer.readline()
            if len(line) == 0 or line.isspace():
                continue
                
            log.append(
                {"role": "user", "content": line.decode("utf-8").strip()}
            )
            
            request_response(log)
            
        except KeyboardInterrupt:
            break

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--preprompt", type=str, default="")
    parser.add_argument("--prompt_suffix", type=str, default="")
    parser.add_argument("--key", type=str, default="")
    parser.add_argument("--cli", action='store_true', help='if you run from python directly')
    args = parser.parse_args()

    #model = GPT4All("ggml-gpt4all-j-v1.3-groovy.bin")
    #model = GPT4All('orca-mini-3b.ggmlv3.q4_0.bin') #bad
    #model = GPT4All('ggml-mpt-7b-chat') # quite good at the moment for question\answer
    model = GPT4All('ggml-model-gpt4all-falcon-q4_0') # good in describing
    #model = GPT4All('wizardlm-13b-v1.1-superhot-8k.ggmlv3.q4_0') #not working under nodejs
    

    with model.chat_session():
        listen_for_messages(args, model)