import { dotnet } from './_framework/dotnet.js';

const runtime = await dotnet.create();
const api = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);

self.onmessage = (e) => {
  const { id, name, buf } = e.data;
  let out;
  try {
    out = JSON.parse(api.Exports.Analyze(new Uint8Array(buf), name));
  } catch (err) {
    out = { ok: false, error: String(err) };
  }
  self.postMessage({ id, out });
};

self.postMessage({ ready: true });
