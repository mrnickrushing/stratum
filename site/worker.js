// stratum.rushingtechnologies.com: the page and pictures are static assets (public/); the downloads are too big
// for those, so they live in the R2 bucket "stratum-downloads" under v<version>/ and stream from here.
const VERSION = "0.2.0";
const FILES = {
  windows: [`Stratum-${VERSION}-Setup.exe`, "application/vnd.microsoft.portable-executable"],
  "windows-zip": [`Stratum-${VERSION}-Windows.zip`, "application/zip"],
  linux: [`Stratum-${VERSION}-Linux.tar.gz`, "application/gzip"],
  sums: ["SHA256SUMS.txt", "text/plain; charset=utf-8"],
};

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const m = url.pathname.match(/^\/download\/([a-z-]+)\/?$/);
    if (!m) return env.ASSETS.fetch(request);
    const f = FILES[m[1]];
    if (!f) return new Response("Not found", { status: 404 });
    if (request.method !== "GET" && request.method !== "HEAD") return new Response("Method not allowed", { status: 405 });
    const [name, type] = f;
    const obj = await env.FILES.get(`v${VERSION}/${name}`, { range: request.headers, onlyIf: request.headers });
    if (!obj) return new Response("Not found", { status: 404 });
    const h = new Headers();
    obj.writeHttpMetadata(h);
    h.set("content-type", type);
    h.set("etag", obj.httpEtag);
    h.set("accept-ranges", "bytes");
    h.set("cache-control", "public, max-age=300");
    if (m[1] !== "sums") h.set("content-disposition", `attachment; filename="${name}"`);
    if (!("body" in obj)) return new Response(null, { status: 304, headers: h });   // onlyIf matched an etag
    let status = 200;
    if (obj.range && request.headers.has("range")) {
      const { offset = 0, length = obj.size - offset } = obj.range;
      h.set("content-range", `bytes ${offset}-${offset + length - 1}/${obj.size}`);
      h.set("content-length", String(length));
      status = 206;
    } else h.set("content-length", String(obj.size));
    return new Response(request.method === "HEAD" ? null : obj.body, { status, headers: h });
  },
};
