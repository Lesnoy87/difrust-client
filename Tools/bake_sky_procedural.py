"""Procedural deep-sky cubemap for DifRUST.

Layers, baked separately and then combined:
  1. deep-space background (dark blue-violet, faint large-scale nebulosity);
  2. Milky Way glow: warped galactic band, warm core, cool arms, wide halo;
  3. dust lanes and dark rifts with sharper edges;
  4. unresolved star clouds (granular texture inside the band);
  5. emission/reflection nebulae and a few distant galaxies;
  6. individual stars: realistic magnitudes, colours, halos, spikes,
     plus a few open clusters.

The band is warped in latitude and modulated along longitude, so it does not
read as a perfect ring.  Faces are 2048x2048; stars are drawn at that
resolution with a tight PSF, so a scope shows pinpoints instead of blobs.

Output: Assets/Sky/mw_px|nx|py|ny|pz|nz.png (2048x2048, top-up)
Preview: Art/sky/preview-proc.png (equirect 2048x1024)

Run: py -3 DifRUST\\Tools\\bake_sky_procedural.py
"""

import math
import os

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Sky")
PREVIEW = os.path.join(ROOT, "Art", "sky", "preview-proc.png")

N = 2048
TILT_DEG = 58.0
AZ_DEG = 25.0
LON_OFFSET_DEG = 12.0
STARS = 220000
CLUSTERS = 6
SEED = 7
FACES = ["px", "nx", "py", "ny", "pz", "nz"]


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# ---------- 3D value noise ----------

G = 96
_rng = np.random.default_rng(SEED)
_grid = (_rng.random((G, G, G)).astype(np.float32) * 2.0 - 1.0)


def trilinear(p):
    p = np.mod(p, G).astype(np.float32)
    i0 = np.floor(p).astype(np.int32)
    f = p - i0
    f = f * f * (3.0 - 2.0 * f)
    i0 = np.mod(i0, G)
    i1 = np.mod(i0 + 1, G)

    x0, y0, z0 = i0[..., 0], i0[..., 1], i0[..., 2]
    x1, y1, z1 = i1[..., 0], i1[..., 1], i1[..., 2]
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]

    c000 = _grid[x0, y0, z0]
    c100 = _grid[x1, y0, z0]
    c010 = _grid[x0, y1, z0]
    c110 = _grid[x1, y1, z0]
    c001 = _grid[x0, y0, z1]
    c101 = _grid[x1, y0, z1]
    c011 = _grid[x0, y1, z1]
    c111 = _grid[x1, y1, z1]

    c00 = c000 + (c100 - c000) * fx
    c10 = c010 + (c110 - c010) * fx
    c01 = c001 + (c101 - c001) * fx
    c11 = c011 + (c111 - c011) * fx

    c0 = c00 + (c10 - c00) * fy
    c1 = c01 + (c11 - c01) * fy

    return c0 + (c1 - c0) * fz


def fbm(d, scale, octaves=4):
    total = np.zeros(d.shape[:-1], dtype=np.float32)
    amp = 1.0
    norm = 0.0
    s = scale

    for o in range(octaves):
        p = d * s + (o * 11.37 + 4.7)
        total += amp * trilinear(p)
        norm += amp
        amp *= 0.5
        s *= 2.07

    return total / norm


# ---------- galactic frame ----------

def build_frame():
    tilt = math.radians(TILT_DEG)
    az = math.radians(AZ_DEG)
    pole = np.array([
        math.sin(tilt) * math.cos(az),
        math.cos(tilt),
        math.sin(tilt) * math.sin(az),
    ], dtype=np.float32)
    pole /= np.linalg.norm(pole)

    ref = np.array([0.0, 0.0, 1.0], dtype=np.float32)
    ex = np.cross(pole, ref)
    ex /= np.linalg.norm(ex)
    ez = np.cross(ex, pole)
    return np.stack([ex, pole, ez], axis=1)


FRAME = build_frame()


def galactic(d):
    g = d @ FRAME
    lat = np.arcsin(np.clip(g[..., 1], -1.0, 1.0))
    lon = np.arctan2(g[..., 0], g[..., 2]) + math.radians(LON_OFFSET_DEG)
    # Лёгкая волна плоскости: полоса перестаёт быть идеальным кольцом.
    lat = lat - (0.050 * np.sin(2.0 * lon + 0.7)
                 + 0.022 * np.sin(3.0 * lon - 1.1))
    return lat, lon


def dust_field(d, lat):
    band = np.exp(-np.square(lat / 0.15))
    n1 = fbm(d, 8.5, 6)
    n2 = fbm(d, 24.0, 4)
    dust = smoothstep(-0.04, 0.52, n1) * smoothstep(-0.22, 0.62, n2)
    return np.clip(dust * band * 1.25, 0.0, 1.0)


def nebula(d, lon0, lat0, size, color, strength, scale):
    g = d @ FRAME
    lat = np.arcsin(np.clip(g[..., 1], -1.0, 1.0))
    lon = np.arctan2(g[..., 0], g[..., 2]) + math.radians(LON_OFFSET_DEG)
    ang = np.sqrt((lon - lon0) ** 2 + (lat - lat0) ** 2)
    shape = np.exp(-np.square(ang / size))
    shape *= 0.35 + 0.65 * (0.5 + 0.5 * fbm(d, scale, 4))
    return color[None, :] * (shape * strength)[..., None]


def galaxy(d, center, size, color, brightness, angle):
    center = center / np.linalg.norm(center)
    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(float(center @ ref)) > 0.95:
        ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    e1 = np.cross(center, ref)
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(center, e1)
    ca, sa = math.cos(angle), math.sin(angle)
    a1 = e1 * ca + e2 * sa
    a2 = e2 * ca - e1 * sa

    x = (d @ a1) / size
    y = (d @ a2) / (size * 0.45)
    r2 = x * x + y * y
    disc = np.exp(-r2)
    core = np.exp(-r2 * 7.0)
    return color[None, :] * ((disc * 0.55 + core * 0.9) * brightness)[..., None]


def sky_rgb(d):
    lat, lon = galactic(d)
    ridge = np.exp(-np.square(lat / 0.055))
    mid = np.exp(-np.square(lat / 0.17))
    halo = np.exp(-np.square(lat / 0.42))
    core = np.exp(-np.square(lon / 0.55) - np.square(lat / 0.09))

    long_var = 0.60 + 0.40 * (0.5 + 0.5 * fbm(d, 2.3, 4))
    clouds = 0.35 + 0.65 * fbm(d, 5.2, 6)
    fine = 0.45 + 0.55 * fbm(d, 19.0, 5)
    gran = 0.55 + 0.45 * fbm(d, 62.0, 3)
    dust = dust_field(d, lat)

    lum = mid * (0.35 + 0.95 * clouds) * (0.45 + 0.55 * fine) \
        * (0.80 + 0.20 * gran) * long_var
    lum += ridge * 0.45 * (0.50 + 0.50 * gran)
    lum += core * 0.75 * (0.55 + 0.45 * gran)
    lum += halo * 0.07

    warm = np.array([1.00, 0.76, 0.50], dtype=np.float32)
    cool = np.array([0.40, 0.53, 0.82], dtype=np.float32)
    pale = np.array([0.83, 0.86, 0.94], dtype=np.float32)

    core_mix = np.clip(core * 1.5 + (long_var - 0.60) * 0.5, 0.0, 1.0)
    tint = cool[None, :] * (1.0 - core_mix[..., None]) \
        + warm[None, :] * core_mix[..., None]
    tint = tint * 0.66 + pale[None, :] * 0.34

    base = np.array([0.010, 0.014, 0.028], dtype=np.float32)
    base = base + 0.006 * fbm(d, 1.4, 3)[..., None]
    base += np.array([0.004, 0.006, 0.012], dtype=np.float32) \
        * smoothstep(-0.5, 1.0, fbm(d, 0.7, 2))[..., None]

    rgb = base + tint * lum[..., None]
    rgb *= (1.0 - 0.88 * dust)[..., None]
    rgb += np.array([0.045, 0.022, 0.012], dtype=np.float32) \
        * (dust * 0.65)[..., None]

    # Тонкие эмиссионные и отражательные туманности.
    rgb += nebula(d, 0.10, 0.02, 0.16, np.array([0.060, 0.012, 0.018],
                                                dtype=np.float32), 0.85, 7.0)
    rgb += nebula(d, 1.85, 0.11, 0.10, np.array([0.015, 0.035, 0.075],
                                                dtype=np.float32), 0.55, 9.0)
    rgb += nebula(d, -1.30, -0.10, 0.065, np.array([0.010, 0.045, 0.045],
                                                   dtype=np.float32), 0.40, 11.0)

    # Две далёкие галактики в стороне от полосы.
    rgb += galaxy(d, np.array([0.35, 0.72, 0.60], dtype=np.float32),
                  0.010, np.array([0.045, 0.040, 0.055], dtype=np.float32),
                  0.55, 0.6)
    rgb += galaxy(d, np.array([-0.62, 0.35, 0.70], dtype=np.float32),
                  0.006, np.array([0.040, 0.045, 0.060], dtype=np.float32),
                  0.45, -0.9)

    return np.clip(rgb, 0.0, 1.0)


# ---------- geometry ----------

def face_dirs(face, n):
    t = (np.arange(n, dtype=np.float32) + 0.5) / n * 2.0 - 1.0
    u, v = np.meshgrid(t, -t)
    o = np.ones_like(u)
    d = {
        0: (o, v, -u),
        1: (-o, v, u),
        2: (u, o, -v),
        3: (u, -o, v),
        4: (u, v, o),
        5: (-u, v, -o),
    }[face]
    d = np.stack(d, axis=-1)
    return d / np.linalg.norm(d, axis=-1, keepdims=True)


def face_of(d):
    x, y, z = float(d[0]), float(d[1]), float(d[2])
    ax = np.abs(d)
    axis = int(np.argmax(ax))
    ma = max(float(ax[axis]), 1e-6)

    if axis == 0:
        if x >= 0:
            face, u = 0, -z / ma
        else:
            face, u = 1, z / ma
        v = y / ma
    elif axis == 1:
        if y >= 0:
            face, v = 2, -z / ma
        else:
            face, v = 3, z / ma
        u = x / ma
    else:
        if z >= 0:
            face, u = 4, x / ma
        else:
            face, u = 5, -x / ma
        v = y / ma

    return face, u, v


# ---------- stars ----------

def unit(v):
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def tangent_dirs(center, count, sigma, rng):
    center = center / np.linalg.norm(center)
    ref = np.array([0.0, 1.0, 0.0], dtype=np.float32)
    if abs(float(center @ ref)) > 0.95:
        ref = np.array([1.0, 0.0, 0.0], dtype=np.float32)
    e1 = np.cross(center, ref)
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(center, e1)
    u = rng.normal(0.0, sigma, count).astype(np.float32)
    v = rng.normal(0.0, sigma, count).astype(np.float32)
    d = center[None, :] + e1[None, :] * u[:, None] + e2[None, :] * v[:, None]
    return unit(d)


def make_stars(rng):
    chunks = []
    total = 0

    while total < STARS:
        n = STARS
        d = unit(rng.normal(size=(n, 3)).astype(np.float32))
        lat, _ = galactic(d)
        p = 0.24 + 0.76 * np.exp(-np.square(lat / 0.24))
        d = d[rng.random(n) < p]

        dust = dust_field(d, galactic(d)[0])
        d = d[rng.random(len(d)) > 0.88 * dust]

        if len(d):
            chunks.append(d)
            total += len(d)

    d = np.concatenate(chunks, axis=0)[:STARS]
    n = len(d)

    mag = 0.08 + 0.92 * rng.random(n) ** 6.4
    mag = mag.astype(np.float32)
    heroes = min(56, n)
    mag[:heroes] = 0.84 + 0.16 * rng.random(heroes)

    u = rng.random(n)
    col = np.empty((n, 3), dtype=np.float32)
    col[:] = (0.97, 0.98, 1.00)
    col[u < 0.56] = (0.78, 0.86, 1.00)
    col[(u >= 0.86) & (u < 0.96)] = (1.00, 0.88, 0.72)
    col[u >= 0.96] = (1.00, 0.70, 0.48)
    col *= (0.90 + 0.20 * rng.random((n, 1))).astype(np.float32)

    # Пара рассеянных скоплений у полосы: чуть плотнее и голубее.
    extra_d = []
    extra_m = []
    extra_c = []
    for i in range(CLUSTERS):
        lon0 = rng.uniform(-math.pi, math.pi)
        lat0 = rng.normal(0.0, 0.10)
        g = np.array([
            math.cos(lat0) * math.sin(lon0),
            math.sin(lat0),
            math.cos(lat0) * math.cos(lon0),
        ], dtype=np.float32)
        center = g @ FRAME.T
        cnt = int(rng.integers(350, 900))
        cd = tangent_dirs(center, cnt, rng.uniform(0.015, 0.045), rng)
        cm = (0.12 + 0.75 * rng.random(cnt) ** 2.5).astype(np.float32)
        cc = np.empty((cnt, 3), dtype=np.float32)
        cc[:] = (0.80, 0.87, 1.00)
        cc *= (0.92 + 0.12 * rng.random((cnt, 1))).astype(np.float32)
        extra_d.append(cd)
        extra_m.append(cm)
        extra_c.append(cc)

    d = np.concatenate([d] + extra_d, axis=0)
    mag = np.concatenate([mag] + extra_m, axis=0)
    col = np.concatenate([col] + extra_c, axis=0)
    return d, mag, col


def splat_star(img, u, v, n, mag, color):
    px = (u + 1.0) * 0.5 * n - 0.5
    py = (1.0 - v) * 0.5 * n - 0.5
    if px < -8 or py < -8 or px > n + 8 or py > n + 8:
        return

    sigma = 0.55 + 1.05 * mag
    amp = 0.12 + 0.88 * mag
    r = int(math.ceil(3.2 * sigma))
    x0 = max(int(math.floor(px)) - r, 0)
    x1 = min(int(math.floor(px)) + r + 1, n)
    y0 = max(int(math.floor(py)) - r, 0)
    y1 = min(int(math.floor(py)) + r + 1, n)
    if x0 >= x1 or y0 >= y1:
        return

    yy, xx = np.mgrid[y0:y1, x0:x1]
    g = np.exp(-((xx - px) ** 2 + (yy - py) ** 2) / (2.0 * sigma * sigma))
    img[y0:y1, x0:x1] += (g * amp)[..., None] * color

    if mag > 0.84:
        hs = 1.6 + 2.2 * (mag - 0.84)
        ha = 0.04 + 0.14 * mag
        hr = int(math.ceil(3.2 * hs))
        hx0 = max(int(math.floor(px)) - hr, 0)
        hx1 = min(int(math.floor(px)) + hr + 1, n)
        hy0 = max(int(math.floor(py)) - hr, 0)
        hy1 = min(int(math.floor(py)) + hr + 1, n)
        if hx0 < hx1 and hy0 < hy1:
            hyy, hxx = np.mgrid[hy0:hy1, hx0:hx1]
            hg = np.exp(-((hxx - px) ** 2 + (hyy - py) ** 2)
                        / (2.0 * hs * hs))
            img[hy0:hy1, hx0:hx1] += (hg * ha)[..., None] * color

    if mag > 0.92:
        length = int(5 + 8 * mag)
        decay = 0.6 + 2.0 * mag
        ca = 0.05 + 0.10 * mag
        for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ts = np.arange(1, length + 1, dtype=np.float32)
            xs = np.round(px + sx * ts).astype(np.int32)
            ys = np.round(py + sy * ts).astype(np.int32)
            ok = (xs >= 0) & (xs < n) & (ys >= 0) & (ys < n)
            if not np.any(ok):
                continue
            vals = ca * np.exp(-ts / decay)
            img[ys[ok], xs[ok]] += vals[ok][:, None] * color


def add_stars(faces, stars):
    n = N
    d, mag, col = stars

    buckets = [[] for _ in range(6)]
    for i in range(len(d)):
        face, u, v = face_of(d[i])
        buckets[face].append((u, v, mag[i], col[i]))

    for f in range(6):
        for u, v, m, c in buckets[f]:
            splat_star(faces[f], u, v, n, m, c)


def cubemap_preview(faces, pw, ph):
    lon = (np.arange(pw, dtype=np.float32) + 0.5) / pw * 2.0 * math.pi - math.pi
    lat = (0.5 - (np.arange(ph, dtype=np.float32) + 0.5) / ph) * math.pi
    lon, lat = np.meshgrid(lon, lat)
    d = np.stack([np.cos(lat) * np.sin(lon),
                  np.sin(lat),
                  np.cos(lat) * np.cos(lon)], axis=-1)

    ax = np.abs(d)
    axis = np.argmax(ax, axis=-1)
    ma = np.maximum(ax.max(axis=-1), 1e-6)

    face = np.zeros(d.shape[:2], dtype=np.int32)
    u = np.zeros(d.shape[:2], dtype=np.float32)
    v = np.zeros(d.shape[:2], dtype=np.float32)

    m = (axis == 0) & (d[..., 0] >= 0)
    face[m] = 0
    u[m] = -d[..., 2][m] / ma[m]
    v[m] = d[..., 1][m] / ma[m]

    m = (axis == 0) & (d[..., 0] < 0)
    face[m] = 1
    u[m] = d[..., 2][m] / ma[m]
    v[m] = d[..., 1][m] / ma[m]

    m = (axis == 1) & (d[..., 1] >= 0)
    face[m] = 2
    u[m] = d[..., 0][m] / ma[m]
    v[m] = -d[..., 2][m] / ma[m]

    m = (axis == 1) & (d[..., 1] < 0)
    face[m] = 3
    u[m] = d[..., 0][m] / ma[m]
    v[m] = d[..., 2][m] / ma[m]

    m = (axis == 2) & (d[..., 2] >= 0)
    face[m] = 4
    u[m] = d[..., 0][m] / ma[m]
    v[m] = d[..., 1][m] / ma[m]

    m = (axis == 2) & (d[..., 2] < 0)
    face[m] = 5
    u[m] = -d[..., 0][m] / ma[m]
    v[m] = d[..., 1][m] / ma[m]

    px = np.clip(np.round((u + 1.0) * 0.5 * N - 0.5), 0, N - 1).astype(np.int32)
    py = np.clip(np.round((1.0 - v) * 0.5 * N - 0.5), 0, N - 1).astype(np.int32)

    out = np.zeros((ph, pw, 3), dtype=np.float32)
    for f in range(6):
        m = face == f
        out[m] = faces[f][py[m], px[m]]
    return np.clip(out, 0.0, 1.0)


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    rng = np.random.default_rng(SEED + 1)

    faces = []
    for f in range(6):
        d = face_dirs(f, N)
        faces.append(sky_rgb(d))
        print("band", FACES[f])

    stars = make_stars(rng)
    print("stars", len(stars[0]))
    add_stars(faces, stars)

    for f in range(6):
        arr = (np.clip(faces[f], 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
        path = os.path.join(OUT, "mw_" + FACES[f] + ".png")
        ok, buf = cv2.imencode(".png", cv2.cvtColor(arr, cv2.COLOR_RGB2BGR))
        buf.tofile(path)
        print("wrote", path)

    prev = cubemap_preview(faces, 2048, 1024)
    prev = (prev * 255.0 + 0.5).astype(np.uint8)
    ok, buf = cv2.imencode(".png", cv2.cvtColor(prev, cv2.COLOR_RGB2BGR))
    buf.tofile(PREVIEW)
    print("wrote", PREVIEW)


if __name__ == "__main__":
    main()
