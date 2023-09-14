from audiocraft.models import AudioGen
from scipy.io.wavfile import write

model = AudioGen.get_pretrained('facebook/audiogen-medium')
model.set_generation_params(
    use_sampling=True,
    top_k=100,
    duration=30
)

output = model.generate(
    descriptions=[
        'relaxing music',
    ],
    progress=True
)
write('test.wav', 16000, output.cpu().numpy())