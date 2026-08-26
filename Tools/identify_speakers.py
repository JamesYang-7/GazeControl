"""Which TalkingWithHands recordings share a speaker.

The corpus identifies no one: a conversation is `trn_2023_v0_NNN` with a
`main-agent` and an `interloctr` side, and nothing says whether the woman in
one recording is the woman in another. For picking the study's five clips that
matters — five conversations that turn out to be three people would waste most
of the variety the selection is trying to buy.

So it is measured. Each side is embedded with ECAPA-TDNN (trained for speaker
verification, so it encodes identity rather than sex or channel) over that
side's own loud half-seconds — the wav tracks are speaker-separated but not
bleed-free, and the quiet frames are mostly the other person.

**The threshold is calibrated, not assumed.** Raw cosine values sit high for
this corpus (one room, one mic setup), so a textbook 0.55 would merge everyone.
Each recording is therefore also embedded twice, from two interleaved halves of
its own speech: same speaker, same channel, same session, so those scores are
what "the same person" looks like *here*. Measured 2026-08-26 over the eight
shortlisted clips: same-speaker control 0.837-0.973 (median 0.955) against a
cross-clip median of 0.130 — two populations with a wide empty gap, so any
threshold in 0.6-0.8 gives the same clustering.

Result for the shortlist (see Assets/Docs/scene1-clip-candidates.md): seven
speakers over eight clips, one woman in six of them, and only five distinct
speaker pairings available.

Needs torch + speechbrain, which the Unity project does not: run it in an
environment that has them, e.g.

    conda run -n pyannote python Tools/identify_speakers.py 008 026 036 040

The checkpoint is fetched from HuggingFace on first use. On Windows,
speechbrain's own loader symlinks out of the HF cache and fails without
elevated privileges, so the model is built from the checkpoint directly.
"""
import argparse
import itertools
from math import gcd
from pathlib import Path

import numpy as np
import torch
from scipy.io import wavfile
from scipy.signal import resample_poly
from speechbrain.lobes.features import Fbank
from speechbrain.lobes.models.ECAPA_TDNN import ECAPA_TDNN
from speechbrain.processing.features import InputNormalization

AUDIO_ROOT = Path("F:/Data/TalkingWithHandsCentered/talkingwithHands-Audio")
SIDES = (("main-agent", "A"), ("interloctr", "B"))
MODEL_DIR = Path.home() / ".cache" / "sb-ecapa-model"
EMBED_SECONDS = 30


def load_model():
    checkpoint = MODEL_DIR / "embedding_model.ckpt"
    if not checkpoint.exists():
        from huggingface_hub import hf_hub_download

        MODEL_DIR.mkdir(parents=True, exist_ok=True)
        downloaded = hf_hub_download("speechbrain/spkrec-ecapa-voxceleb", "embedding_model.ckpt")
        checkpoint.write_bytes(Path(downloaded).read_bytes())

    net = ECAPA_TDNN(input_size=80, channels=[1024, 1024, 1024, 1024, 3072],
                     kernel_sizes=[5, 3, 3, 3, 1], dilations=[1, 2, 3, 4, 1],
                     attention_channels=128, lin_neurons=192)
    net.load_state_dict(torch.load(checkpoint, map_location="cpu", weights_only=True))
    net.eval()
    return net, Fbank(n_mels=80), InputNormalization(norm_type="sentence", std_norm=False)


def own_speech(stem, side, target_sr=16000):
    """This side's loud half-seconds, resampled — its own speaker, not the bleed."""
    path = AUDIO_ROOT / side / "wav" / f"trn_2023_v0_{stem}_{side}.wav"
    sample_rate, samples = wavfile.read(path)
    if samples.ndim > 1:
        samples = samples.mean(axis=1)

    samples = samples.astype(np.float64)
    samples /= np.abs(samples).max() + 1e-12

    window = int(0.5 * sample_rate)
    count = len(samples) // window
    blocks = samples[: count * window].reshape(count, window)
    rms = np.sqrt((blocks ** 2).mean(axis=1))
    loud = blocks[rms > max(np.percentile(rms, 75), 1e-4)]

    divisor = gcd(sample_rate, target_sr)
    return [resample_poly(b, target_sr // divisor, sample_rate // divisor) for b in loud]


def embed(model, blocks):
    net, features, norm = model
    audio = np.concatenate(blocks)[: 16000 * EMBED_SECONDS]
    with torch.no_grad():
        wave = torch.tensor(audio, dtype=torch.float32).unsqueeze(0)
        vector = net(norm(features(wave), torch.ones(1))).squeeze().numpy()

    return vector / np.linalg.norm(vector)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("stems", nargs="+", help="conversation numbers, e.g. 008 036 119")
    parser.add_argument("--threshold", type=float, default=0.70,
                        help="cosine above which two sides are one speaker (default 0.70)")
    args = parser.parse_args()

    model = load_model()
    speech = {f"{stem}{label}": own_speech(stem, side)
              for stem in args.stems for side, label in SIDES}
    embeddings = {key: embed(model, blocks) for key, blocks in speech.items()}

    # Same speaker by construction: two interleaved halves of one recording.
    control = [float(embed(model, blocks[0::2]) @ embed(model, blocks[1::2]))
               for blocks in speech.values()]
    print(f"same-speaker control: {min(control):+.3f} to {max(control):+.3f} "
          f"(median {np.median(control):+.3f}) over {len(control)} recordings")

    keys = sorted(embeddings)
    pairs = sorted(((float(embeddings[a] @ embeddings[b]), a, b)
                    for a, b in itertools.combinations(keys, 2)), reverse=True)
    print(f"cross-clip pairs:     median {np.median([p[0] for p in pairs]):+.3f}, "
          f"min {pairs[-1][0]:+.3f}\n")

    if np.median([p[0] for p in pairs]) > args.threshold:
        print("WARNING: most cross-clip pairs are above the threshold, so it is too low "
              "for this material — compare against the control range above.\n")

    print(f"pairs above {args.threshold:.2f}:")
    for score, a, b in pairs:
        if score < args.threshold:
            break
        print(f"    {a} = {b}   {score:+.3f}")

    parent = {key: key for key in keys}

    def root(key):
        while parent[key] != key:
            parent[key] = parent[parent[key]]
            key = parent[key]
        return key

    for score, a, b in pairs:
        if score < args.threshold:
            break
        parent[root(a)] = root(b)

    groups = {}
    for key in keys:
        groups.setdefault(root(key), []).append(key)

    print(f"\n{len(groups)} distinct speakers across {len(args.stems)} clips:")
    for index, group in enumerate(sorted(groups.values(), key=lambda g: (-len(g), sorted(g)[0])), 1):
        group = sorted(group)
        # Single-link clustering can chain, so report the weakest pair inside
        # each group: if it sits below the control range, the group is a chain.
        weakest = min((float(embeddings[a] @ embeddings[b])
                       for a, b in itertools.combinations(group, 2)), default=1.0)
        print(f"    S{index}: {', '.join(group):45s} weakest internal pair {weakest:+.3f}")


if __name__ == "__main__":
    main()
