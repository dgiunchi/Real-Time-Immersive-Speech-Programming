import torch
from audiocraft.models import AudioGen
from audiocraft.models import AudioGen
from audiocraft.utils.notebook import display_audio
from scipy.io.wavfile import write


import hashlib
import os
import sys
import json
import argparse


#display_audio(output, sample_rate=16000)


parser = argparse.ArgumentParser()
parser.add_argument("--model", type=str, default="facebook/audiogen-medium")
parser.add_argument("--output_folder", type=str, default="./output")
parser.add_argument("--prompt_postfix", type=str, default="")
args = parser.parse_args()

queue = []
model = None
busy = False
done = False


def generateAudioFromPrompt(model, message):
    global busy
    message = message.decode()
    if message != "" and busy == False:
        busy = True
        # Get dict from JSON string in prompt
        message = json.loads(message)
        prompt = message["prompt"]
        file_name = message["output_file"] + ".wav"

        prompt += args.prompt_postfix
        
        
        output = model.generate(
            descriptions=[
                prompt,
            ],
            progress=True
        )
        
        fullpath = os.path.join(args.output_folder, file_name)
        write(fullpath, 16000, output.cpu().numpy())
        del model
        print(file_name)
        busy = False
        


def recognize_from_stdin():
    global model,  done, busy

    if model == None:
        print(
            "Initializing audio generation model "
            + args.model
            + " (output folder: "
            + args.output_folder
            + "."
        )
        model = AudioGen.get_pretrained(args.model)
        model.set_generation_params(
            use_sampling=True,
            top_k=100,
            duration=5
        )

    # Write stdin to the stream
    while not done:
        try:
            line = sys.stdin.buffer.readline()
            if len(line) == 0:
                continue
            generateAudioFromPrompt(model, line)
        except KeyboardInterrupt:
            break


recognize_from_stdin()
