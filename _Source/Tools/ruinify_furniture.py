"""Generate ruined variants of the DMS furniture sprites in the Ruin_Building palette.

Style reference: Textures/Things/Building/Ruin_Building/* -- fully desaturated, darkened body,
flat hard-edged rust/grime blobs in dark brown, a few burnt-black patches, chipped edges
re-outlined in black.
"""
import os, sys, glob, zlib
import numpy as np
from PIL import Image, ImageFilter

SRC_ROOT = sys.argv[1]
DST_ROOT = sys.argv[2]
VARIANTS = ["A", "B", "C"]
PX_PER_CELL = 256

# palette sampled from Ruin_Building (see conversation)
RUST_LIGHT = np.array([72, 64, 56])
RUST_DARK = np.array([56, 48, 40])
GRIME = np.array([40, 32, 32])
BURNT = np.array([16, 16, 16])
OUTLINE = np.array([0, 0, 0])

SETS = {
    "DMS_Personallocker": "DMS_Personallocker/DMS_1X1_Personallocker",
    "DMS_Personallocker_Large": "DMS_Personallocker_Large/DMS_3X1_Personallocker",
    "DMS_Waterdispenser": "DMS_Waterdispenser/DMS_1X1_Waterdispenser",
    "DMS_Benches": "DMS_Benches/DMS_3X1_Benches",
    "DMS_Airpurifier": "DMS_Airpurifier/DMS_Airpurifier",
    "DMS_Hydroponics_Flowerpot": "DMS_Hydroponics_Flowerpot/DMS_Hydroponics_Flowerpot",
}


def value_noise(rng, w, h, scale):
    """Smooth blobby noise in [0,1]; `scale` is the blob size in px."""
    small_w, small_h = max(3, int(w / scale * 1.5)), max(3, int(h / scale * 1.5))
    base = rng.random((small_h, small_w)).astype(np.float32)
    im = Image.fromarray((base * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    im = im.filter(ImageFilter.GaussianBlur(scale * 0.35))
    n = np.asarray(im).astype(np.float32) / 255.0
    return (n - n.min()) / max(1e-6, (n.max() - n.min()))


def morph(mask, radius, grow):
    """Square erosion/dilation via box blur (O(1) per pixel, unlike Min/MaxFilter)."""
    if radius < 1:
        return mask.copy()
    im = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(radius))
    arr = np.asarray(im)
    return arr > 0 if grow else arr >= 254


def largest_component(mask):
    """Keep only the connected component containing the most interior pixel."""
    from PIL import ImageDraw
    core = mask
    r = 2
    while core.sum() > 64 and r < 512:
        nxt = morph(mask, r, grow=False)
        if nxt.sum() == 0:
            break
        core, r = nxt, r + 2
    ys, xs = np.where(core)
    i = len(xs) // 2
    seed = (int(xs[i]), int(ys[i]))
    im = Image.fromarray((mask * 255).astype(np.uint8)).copy()  # fromarray buffers are read-only
    ImageDraw.floodfill(im, seed, 128)
    return np.asarray(im) == 128


def outline_mask(lum):
    return lum < 24


def ruinify(src, seed):
    rng = np.random.default_rng(seed)
    a = np.array(Image.open(src).convert("RGBA")).astype(np.float32)
    h, w = a.shape[:2]
    rgb, alpha = a[..., :3], a[..., 3]
    solid = alpha > 127
    ys, xs = np.where(solid)
    obj = float(min(xs.max() - xs.min(), ys.max() - ys.min()))  # object's short side, px
    # original outline thickness: black run length measured inward from the left edge
    lum = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    runs = []
    for y in range(ys.min(), ys.max(), max(1, (ys.max() - ys.min()) // 40)):
        row = np.where(solid[y])[0]
        if len(row) == 0:
            continue
        x0 = row[0]; r = 0
        while x0 + r < w and solid[y, x0 + r] and lum[y, x0 + r] < 40:
            r += 1
        if r:
            runs.append(r)
    ol = int(np.median(runs)) if runs else int(obj * 0.04)
    ol = max(2, min(ol, int(obj * 0.08)))

    # --- 1. desaturate + darken to the ruin grey ramp (outline stays pure black)
    grey = np.where(lum < 24, 0, lum * 0.62 + 25)
    grey = np.floor(grey / 8) * 8  # flat, posterised like the hand-painted set
    out = np.stack([grey, grey, grey], axis=-1)
    warm = grey > 100  # lighter panels pick up a faint warm tint in the reference
    out[warm] = out[warm] - np.array([0, 6, 8])
    outline = lum < 24

    # --- 2. rust / grime, biased toward the seams between flat colour blocks
    # seams = boundaries between posterised colours (panel lines, outline, screen frames)
    q = np.floor(lum / 24)
    seams = np.zeros_like(solid)
    seams[1:, :] |= q[1:, :] != q[:-1, :]
    seams[:, 1:] |= q[:, 1:] != q[:, :-1]
    seams &= solid
    # proximity field: 1 on a seam, fading out over ~5% of the object size
    prox_im = Image.fromarray((seams * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(obj * 0.05))
    prox = np.asarray(prox_im).astype(np.float32)
    prox = np.clip(prox / max(1.0, np.quantile(prox[solid], 0.98)), 0, 1)

    n = 0.8 * value_noise(rng, w, h, obj * 0.45) + 0.2 * value_noise(rng, w, h, obj * 0.12)
    n = 0.72 * n + 0.28 * prox  # blobs still random, but they cling to seams and edges
    rust = n > np.quantile(n[solid], 0.72)
    rust_dark = n > np.quantile(n[solid], 0.88)
    g = value_noise(rng, w, h, obj * 0.10)
    grime = g > np.quantile(g[solid], 0.965)
    b = value_noise(rng, w, h, obj * 0.30)
    burnt = b > np.quantile(b[solid], 0.955)
    # seam grime: broken dark lines hugging the panel joints (gated by fine noise)
    seam_band = morph(seams, max(1, int(obj * 0.012)), grow=True) & ~morph(outline_mask(lum), 1, grow=True)
    fine = value_noise(rng, w, h, obj * 0.14)  # coarse gate -> longer unbroken runs, fewer specks
    seam_grime = seam_band & (fine > np.quantile(fine[solid], 0.62))
    seam_rust = seam_band & (fine > np.quantile(fine[solid], 0.88))

    paint = solid & ~outline
    out[paint & rust] = RUST_LIGHT
    out[paint & rust_dark] = RUST_DARK
    out[paint & seam_grime] = RUST_DARK
    out[paint & seam_rust] = GRIME
    out[paint & grime] = GRIME
    out[paint & burnt] = BURNT

    # --- 3. chip the silhouette: a handful of bites out of an edge band, re-outlined in black
    band = max(ol * 2, int(obj * 0.12))
    edge_band = solid & ~morph(solid, band, grow=False)
    c = value_noise(rng, w, h, obj * 0.18)
    chips = edge_band & (c > np.quantile(c[edge_band], 0.90))
    # round the bites off (box morphology alone leaves square notches)
    chips_im = Image.fromarray((chips * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(max(1.0, ol * 0.8)))
    chips = np.asarray(chips_im) > 110
    new_solid = solid & ~chips
    new_solid = morph(morph(new_solid, 2, grow=False), 2, grow=True)  # clean specks
    new_solid = largest_component(new_solid)  # drop islands cut loose by the chips
    ring = new_solid & ~morph(new_solid, ol, grow=False)
    cut_ring = ring & morph(chips, ol, grow=True)
    out[cut_ring] = OUTLINE

    alpha_out = np.where(new_solid, alpha, 0)
    res = np.concatenate([np.clip(out, 0, 255), alpha_out[..., None]], axis=-1).astype(np.uint8)
    return Image.fromarray(res, "RGBA")


count = 0
for name, base in SETS.items():
    for src in sorted(glob.glob(os.path.join(SRC_ROOT, base + "_*.png"))):
        rot = os.path.basename(src).rsplit("_", 1)[1][:-4]
        for vi, v in enumerate(VARIANTS):
            dst = os.path.join(DST_ROOT, name, f"{v}_{rot}.png")
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            # same seed across rotations of one variant so the damage reads as one object
            ruinify(src, seed=zlib.crc32(f"{name}_{v}".encode())).save(dst)
            count += 1
            print(dst)
print("generated", count)
