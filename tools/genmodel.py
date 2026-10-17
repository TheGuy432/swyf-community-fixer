# Generate a full-size whisper ggml f16 model with random weights (real tensor names/shapes),
# using the header/mel/vocab section of whisper.cpp's for-tests model. For testing the quantizer.
import struct, sys
import numpy as np

src, dst, seed = sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1
head = open(src, 'rb').read()
h = struct.unpack('<12i', head[:48])
n_vocab, a_ctx, a_state, a_head, a_layer, t_ctx, t_state, t_head, t_layer, n_mels = h[1:11]
rng = np.random.default_rng(seed)

def rnd(shape_ne, f16):
    n = int(np.prod(shape_ne))
    x = rng.standard_normal(n).astype(np.float32) * 0.05
    # edge cases: all-zero block, huge values, fp16 subnormals, exact ties, negative max
    if n >= 32 * 8:
        x[0:32] = 0
        x[32:64] = rng.standard_normal(32) * 3000
        x[64:96] = rng.standard_normal(32) * 1e-6
        x[96:128] = np.arange(-16, 16) * 0.5
        x[128:160] = -1.0; x[130] = -2.0
        x[160:192] = np.float32(65504.0) * np.sign(rng.standard_normal(32))
    return x.astype(np.float16 if f16 else np.float32)

out = bytearray(head)
def tensor(name, ne, f16):
    d = rnd(ne, f16)
    nb = name.encode()
    out.extend(struct.pack('<3i', len(ne), len(nb), 1 if f16 else 0))
    out.extend(struct.pack('<%di' % len(ne), *ne))
    out.extend(nb); out.extend(d.tobytes())

S, T = a_state, t_state
tensor("decoder.positional_embedding", [T, t_ctx], False)
tensor("decoder.token_embedding.weight", [T, n_vocab], True)
def block(prefix, s, cross):
    attns = ["attn"] + (["cross_attn"] if cross else [])
    for a in attns:
        tensor(f"{prefix}.{a}.query.weight", [s, s], True); tensor(f"{prefix}.{a}.query.bias", [s], False)
        tensor(f"{prefix}.{a}.key.weight", [s, s], True)
        tensor(f"{prefix}.{a}.value.weight", [s, s], True); tensor(f"{prefix}.{a}.value.bias", [s], False)
        tensor(f"{prefix}.{a}.out.weight", [s, s], True); tensor(f"{prefix}.{a}.out.bias", [s], False)
        tensor(f"{prefix}.{a}_ln.weight", [s], False); tensor(f"{prefix}.{a}_ln.bias", [s], False)
    tensor(f"{prefix}.mlp.0.weight", [s, 4 * s], True); tensor(f"{prefix}.mlp.0.bias", [4 * s], False)
    tensor(f"{prefix}.mlp.2.weight", [4 * s, s], True); tensor(f"{prefix}.mlp.2.bias", [s], False)
    tensor(f"{prefix}.mlp_ln.weight", [s], False); tensor(f"{prefix}.mlp_ln.bias", [s], False)
for i in range(t_layer): block(f"decoder.blocks.{i}", T, True)
tensor("decoder.ln.weight", [T], False); tensor("decoder.ln.bias", [T], False)
tensor("encoder.positional_embedding", [S, a_ctx], False)
tensor("encoder.conv1.weight", [3, n_mels, S], True); tensor("encoder.conv1.bias", [1, S], False)
tensor("encoder.conv2.weight", [3, S, S], True); tensor("encoder.conv2.bias", [1, S], False)
for i in range(a_layer): block(f"encoder.blocks.{i}", S, False)
tensor("encoder.ln_post.weight", [S], False); tensor("encoder.ln_post.bias", [S], False)
open(dst, 'wb').write(out)
print(dst, len(out))
