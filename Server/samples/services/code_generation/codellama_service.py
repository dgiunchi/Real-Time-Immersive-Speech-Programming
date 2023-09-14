import json
import sys
from transformers import AutoTokenizer
import transformers
import torch
import argparse
import re
import time

pattern_f = r'<<ANS>>'
pattern = r'<<ANS>>(.*?)<<ANS>>'


def request_response(log, pipeline, tokenizer):
    # NON real time
    system = log[-1]['system']
    user = log[-1]['user']
    prompt = f"<s><<SYS>>\n{system}\n<</SYS>>\n\n{user}" #pay attention that prompt could be different 

    start = time.time()
    sequences = pipeline(prompt,do_sample=True, top_k=10, temperature=0.1, top_p=0.95, num_return_sequences=1, eos_token_id=tokenizer.eos_token_id, max_length=200,add_special_tokens=False)
    #print(time.time() - start)
    
    first_match = re.search(pattern_f, sequences[0]['generated_text'], re.DOTALL)
    text_after_first_ANS = sequences[0]['generated_text'][first_match.end():]
    matches = re.findall(pattern, text_after_first_ANS, re.DOTALL)
    
    log[-1]["response"] = matches[0]
    print(">" + log[-1]["response"])
    


def listen_for_messages(args, pipeline, tokenizer):
    log = []# list of {"system": "", "user":"", "response":""}  #this is how is formatted the log
    
    if args.cli : print("Ready...")
    
    system = "provide answers in c#, put the code between two <<ANS>>" #could be parametrised
    while True:
        try:
            line = sys.stdin.buffer.readline()
            if len(line) == 0 or line.isspace():
                continue
                
            log.append(
                {"system": system, "user": line.decode("utf-8").strip(), "response": ""}
            )
            
            request_response(log, pipeline, tokenizer)
            
        except KeyboardInterrupt:
            break

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--preprompt", type=str, default="")
    parser.add_argument("--prompt_suffix", type=str, default="")
    parser.add_argument("--key", type=str, default="")
    parser.add_argument("--cli", action='store_true', help='if you run from python directly')
    args = parser.parse_args()

    model = "codellama/CodeLlama-7b-Instruct-hf" #this is not the basic
    tokenizer = AutoTokenizer.from_pretrained(model)
    pipeline = transformers.pipeline("text-generation", model=model, torch_dtype=torch.float16,device_map="auto")

    listen_for_messages(args, pipeline, tokenizer)