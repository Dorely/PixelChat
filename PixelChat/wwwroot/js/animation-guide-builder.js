import * as THREE from "/lib/three/build/three.module.js?v=2";
import { GLTFLoader } from "/lib/three/examples/jsm/loaders/GLTFLoader.js?v=2";

const viewers = new WeakMap();

export async function startViewer(host, options, dotNetRef) {
    dispose(host);
    if (!host) {
        return;
    }

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x111827, 1);
    host.innerHTML = "";
    host.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(35, 1, 0.1, 100);
    camera.position.set(0, 1.35, 4.2);
    camera.lookAt(0, 0.9, 0);

    const pivot = new THREE.Group();
    scene.add(pivot);
    scene.add(new THREE.HemisphereLight(0xf8fafc, 0x243041, 2.2));
    const key = new THREE.DirectionalLight(0xffffff, 2.6);
    key.position.set(2.5, 4, 3);
    scene.add(key);
    const fill = new THREE.DirectionalLight(0xbfd7ff, 1.1);
    fill.position.set(-3, 2, -2);
    scene.add(fill);

    const state = {
        host,
        renderer,
        scene,
        camera,
        pivot,
        mixer: null,
        fallbackParts: [],
        animationFrame: null,
        resizeObserver: null,
        lastTime: performance.now(),
        yaw: normalizeYaw(Number(options?.yaw ?? 90)),
        playing: Boolean(options?.playing ?? true),
        dragging: false,
        lastX: 0,
        dotNetRef,
        disposed: false,
    };
    viewers.set(host, state);

    bindPointer(state);
    bindResize(state);
    resize(state);

    try {
        await loadGltf(state, options?.assetUrl, options?.animationName);
    } catch {
        buildFallbackMannequin(state);
    }

    if (state.disposed || viewers.get(host) !== state) {
        return;
    }

    setYaw(host, state.yaw);
    animate(state);
}

export function setYaw(host, yaw) {
    const state = viewers.get(host);
    if (!state) {
        return;
    }

    state.yaw = normalizeYaw(Number(yaw) || 0);
    state.pivot.rotation.y = THREE.MathUtils.degToRad(state.yaw);
}

export function setPlaying(host, playing) {
    const state = viewers.get(host);
    if (!state) {
        return;
    }

    state.playing = Boolean(playing);
}

export function dispose(host) {
    const state = viewers.get(host);
    if (!state) {
        return;
    }

    state.disposed = true;
    if (state.animationFrame !== null) {
        cancelAnimationFrame(state.animationFrame);
        state.animationFrame = null;
    }
    if (state.resizeObserver) {
        state.resizeObserver.disconnect();
        state.resizeObserver = null;
    }

    state.renderer.dispose();
    disposeObject(state.scene);
    if (state.renderer.domElement.parentElement === host) {
        host.removeChild(state.renderer.domElement);
    }
    viewers.delete(host);
}

async function loadGltf(state, assetUrl, animationName) {
    if (!assetUrl) {
        throw new Error("Missing asset URL.");
    }

    const loader = new GLTFLoader();
    const gltf = await loader.loadAsync(assetUrl);
    const model = gltf.scene;
    normalizeModel(model);
    state.pivot.add(model);

    const clip = gltf.animations.find(item => item.name === animationName) ?? gltf.animations[0];
    if (clip) {
        state.mixer = new THREE.AnimationMixer(model);
        const action = state.mixer.clipAction(clip);
        action.reset();
        action.play();
    }
}

function normalizeModel(model) {
    const box = new THREE.Box3().setFromObject(model);
    const size = new THREE.Vector3();
    const center = new THREE.Vector3();
    box.getSize(size);
    box.getCenter(center);

    const maxDimension = Math.max(size.x, size.y, size.z, 0.001);
    const scale = 2.2 / maxDimension;
    model.scale.multiplyScalar(scale);
    model.position.sub(center.multiplyScalar(scale));
    model.position.y += 1.1;
}

function buildFallbackMannequin(state) {
    const material = new THREE.MeshStandardMaterial({ color: 0xd7e5ff, roughness: 0.72, metalness: 0.05 });
    const jointMaterial = new THREE.MeshStandardMaterial({ color: 0x8ab4f8, roughness: 0.7 });
    const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.32, 0.72, 8, 16), material);
    torso.position.y = 1.25;
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.23, 24, 16), jointMaterial);
    head.position.y = 1.9;
    const hips = new THREE.Mesh(new THREE.SphereGeometry(0.22, 20, 12), jointMaterial);
    hips.position.y = 0.78;
    state.pivot.add(torso, head, hips);
    state.fallbackParts.push(
        addLimb(state.pivot, -0.34, 1.45, -0.1, 0.65, material),
        addLimb(state.pivot, 0.34, 1.45, 0.1, 0.65, material),
        addLimb(state.pivot, -0.18, 0.58, 0.15, 0.78, material),
        addLimb(state.pivot, 0.18, 0.58, -0.15, 0.78, material));
}

function addLimb(root, x, y, zRotation, length, material) {
    const limb = new THREE.Mesh(new THREE.CapsuleGeometry(0.075, length, 8, 10), material);
    limb.position.set(x, y, 0);
    limb.rotation.z = zRotation;
    root.add(limb);
    return limb;
}

function animate(state) {
    if (state.disposed) {
        return;
    }

    const now = performance.now();
    const delta = Math.min(0.08, (now - state.lastTime) / 1000);
    state.lastTime = now;

    if (state.playing) {
        state.mixer?.update(delta);
        if (state.fallbackParts.length) {
            const t = now / 220;
            state.fallbackParts.forEach((part, index) => {
                part.rotation.z = (index % 2 === 0 ? -0.24 : 0.24) + Math.sin(t + index * Math.PI) * 0.34;
            });
        }
    }

    state.renderer.render(state.scene, state.camera);
    state.animationFrame = requestAnimationFrame(() => animate(state));
}

function bindResize(state) {
    if (typeof ResizeObserver === "function") {
        state.resizeObserver = new ResizeObserver(() => resize(state));
        state.resizeObserver.observe(state.host);
    } else {
        window.addEventListener("resize", () => resize(state), { passive: true });
    }
}

function resize(state) {
    const rect = state.host.getBoundingClientRect();
    const width = Math.max(1, Math.round(rect.width || 1));
    const height = Math.max(1, Math.round(rect.height || 1));
    state.camera.aspect = width / height;
    state.camera.updateProjectionMatrix();
    state.renderer.setSize(width, height, false);
}

function bindPointer(state) {
    const canvas = state.renderer.domElement;
    canvas.addEventListener("pointerdown", event => {
        state.dragging = true;
        state.lastX = event.clientX;
        canvas.setPointerCapture?.(event.pointerId);
    });
    canvas.addEventListener("pointermove", event => {
        if (!state.dragging) {
            return;
        }

        const delta = event.clientX - state.lastX;
        state.lastX = event.clientX;
        state.yaw = normalizeYaw(state.yaw + delta * 0.35);
        state.pivot.rotation.y = THREE.MathUtils.degToRad(state.yaw);
        state.dotNetRef?.invokeMethodAsync("OnAnimationGuideYawChanged", state.yaw);
    });
    const end = event => {
        if (!state.dragging) {
            return;
        }

        state.dragging = false;
        canvas.releasePointerCapture?.(event.pointerId);
        state.dotNetRef?.invokeMethodAsync("OnAnimationGuideYawChanged", state.yaw);
    };
    canvas.addEventListener("pointerup", end);
    canvas.addEventListener("pointercancel", end);
}

function normalizeYaw(yaw) {
    let normalized = yaw % 360;
    if (normalized > 180) {
        normalized -= 360;
    }
    if (normalized < -180) {
        normalized += 360;
    }
    return Math.round(normalized * 100) / 100;
}

function disposeObject(object) {
    object.traverse(child => {
        if (child.geometry) {
            child.geometry.dispose();
        }
        if (child.material) {
            const materials = Array.isArray(child.material) ? child.material : [child.material];
            materials.forEach(material => material.dispose?.());
        }
    });
}
