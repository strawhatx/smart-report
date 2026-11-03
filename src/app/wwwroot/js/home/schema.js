/**
 * The signature scene: a wireframe schema seen in depth, with a query path that traces through
 * it as you scroll, and tables that shut when the viewer isn't cleared for them.
 *
 * Budget, deliberately small:
 *  - Line geometry only. No fills, no lights, no post-processing.
 *  - One shared material per state, so the whole scene is a handful of draw calls.
 *  - Device pixel ratio capped at 2.
 *  - The render loop is paused whenever the canvas is off-screen.
 *  - Everything is disposed on pagehide.
 *
 * It is imported dynamically and only on devices that passed the capability check, so most
 * visitors never download Three.js at all.
 */

import * as THREE from '../../lib/three/three.module.min.js';

/** Tables, their depth, and whether a regional manager may read them. */
const SLABS = [
  { label: 'orders',       x: -2.1, y: 0.9,  z: 0.0,  w: 2.3, h: 1.3, restricted: false },
  { label: 'order_items',  x: 0.9,  y: 1.3,  z: -1.8, w: 2.1, h: 1.1, restricted: false },
  { label: 'regions',      x: 2.6,  y: -0.6, z: -0.6, w: 1.6, h: 0.9, restricted: false },
  { label: 'products',     x: -2.8, y: -1.4, z: -2.6, w: 1.8, h: 1.0, restricted: false },
  { label: 'employees',    x: 0.4,  y: -1.7, z: -4.0, w: 2.0, h: 1.1, restricted: false },
  { label: 'compensation', x: 3.1,  y: 1.6,  z: -3.2, w: 2.2, h: 1.2, restricted: true  },
  { label: 'attrition',    x: -1.2, y: 2.4,  z: -5.0, w: 1.7, h: 0.9, restricted: true  },
];

/** The route the query takes, in slab indices. */
const PATH = [0, 1, 2];

export function createSchemaScene(canvas, { ink, signal, strike }) {
  const renderer = new THREE.WebGLRenderer({
    canvas,
    antialias: true,
    alpha: true,
    powerPreference: 'high-performance',
  });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 100);
  camera.position.set(0, 0, 7.4);

  const root = new THREE.Group();
  scene.add(root);

  const materials = {
    idle: new THREE.LineBasicMaterial({ color: ink, transparent: true, opacity: 0.26 }),
    live: new THREE.LineBasicMaterial({ color: signal, transparent: true, opacity: 0.95 }),
    shut: new THREE.LineBasicMaterial({ color: strike, transparent: true, opacity: 0.5 }),
  };

  // --- Slabs. Each is an outline plus a few interior ticks suggesting columns.
  const slabs = SLABS.map((spec) => {
    const group = new THREE.Group();
    group.position.set(spec.x, spec.y, spec.z);

    const plane = new THREE.PlaneGeometry(spec.w, spec.h);
    const outline = new THREE.LineSegments(new THREE.EdgesGeometry(plane), materials.idle);
    group.add(outline);
    plane.dispose();

    const ticks = [];
    const rows = 4;
    for (let i = 1; i < rows; i++) {
      const y = spec.h / 2 - (spec.h / rows) * i;
      ticks.push(-spec.w / 2, y, 0, spec.w / 2, y, 0);
    }
    const tickGeometry = new THREE.BufferGeometry();
    tickGeometry.setAttribute('position', new THREE.Float32BufferAttribute(ticks, 3));
    const tickLines = new THREE.LineSegments(tickGeometry, materials.idle);
    group.add(tickLines);

    // The strike drawn across a table the viewer may not read.
    const crossGeometry = new THREE.BufferGeometry();
    crossGeometry.setAttribute('position', new THREE.Float32BufferAttribute([
      -spec.w / 2, -spec.h / 2, 0.01, spec.w / 2, spec.h / 2, 0.01,
      -spec.w / 2, spec.h / 2, 0.01, spec.w / 2, -spec.h / 2, 0.01,
    ], 3));
    const cross = new THREE.LineSegments(crossGeometry, materials.shut);
    cross.visible = false;
    group.add(cross);

    root.add(group);
    return { spec, group, outline, tickLines, cross, geometries: [tickGeometry, crossGeometry] };
  });

  // --- The query path: a tube along a curve through the first three slabs, revealed by scroll.
  const curve = new THREE.CatmullRomCurve3(
    PATH.map((i) => new THREE.Vector3(SLABS[i].x, SLABS[i].y, SLABS[i].z + 0.02)));
  const tubeGeometry = new THREE.TubeGeometry(curve, 96, 0.022, 6, false);
  const tube = new THREE.Mesh(
    tubeGeometry,
    new THREE.MeshBasicMaterial({ color: signal, transparent: true, opacity: 0.9 }));
  tube.visible = false;
  root.add(tube);

  const indexCount = tubeGeometry.index ? tubeGeometry.index.count : 0;

  let progress = 0;      // 0 → 1 across sections 01–05
  let clearance = 1;     // 1 = sees everything, 0 = restricted tables shut
  let running = false;
  let frame = 0;
  let disposed = false;

  function resize() {
    const { clientWidth: w, clientHeight: h } = canvas;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  }

  function render(time) {
    if (disposed) return;
    frame = requestAnimationFrame(render);

    // Camera pushes in through the schema as the story advances.
    camera.position.z = 7.4 - progress * 3.4;
    camera.position.x = Math.sin(time * 0.00013) * 0.22 + progress * 0.5;
    camera.position.y = Math.cos(time * 0.00011) * 0.16;
    camera.lookAt(0, 0, -2);

    root.rotation.y = Math.sin(time * 0.00009) * 0.05 + progress * 0.08;

    // The path ignites partway through and draws itself.
    const pathProgress = Math.max(0, Math.min(1, (progress - 0.18) / 0.42));
    tube.visible = pathProgress > 0;
    if (indexCount) {
      tubeGeometry.setDrawRange(0, Math.floor(indexCount * pathProgress));
    }

    for (const slab of slabs) {
      const onPath = PATH.includes(slabs.indexOf(slab));
      const shut = slab.spec.restricted && clearance < 0.5;

      slab.cross.visible = shut;
      slab.outline.material = shut ? materials.shut
        : onPath && pathProgress > 0.1 ? materials.live
        : materials.idle;
      slab.tickLines.material = shut ? materials.shut : materials.idle;
    }

    renderer.render(scene, camera);
  }

  function start() {
    if (running || disposed) return;
    running = true;
    frame = requestAnimationFrame(render);
  }

  function stop() {
    running = false;
    cancelAnimationFrame(frame);
  }

  // Not rendering a canvas nobody can see is the single biggest win available here.
  const observer = new IntersectionObserver(
    ([entry]) => (entry.isIntersecting ? start() : stop()),
    { threshold: 0 });
  observer.observe(canvas);

  const onResize = () => resize();
  window.addEventListener('resize', onResize, { passive: true });
  resize();

  function dispose() {
    if (disposed) return;
    disposed = true;
    stop();
    observer.disconnect();
    window.removeEventListener('resize', onResize);

    for (const slab of slabs) {
      slab.outline.geometry.dispose();
      slab.geometries.forEach((g) => g.dispose());
    }
    tubeGeometry.dispose();
    tube.material.dispose();
    Object.values(materials).forEach((m) => m.dispose());
    renderer.dispose();
  }

  window.addEventListener('pagehide', dispose, { once: true });

  return {
    setProgress(value) { progress = Math.max(0, Math.min(1, value)); },
    setClearance(value) { clearance = value; },
    dispose,
  };
}
