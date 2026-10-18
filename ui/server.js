// Zero-dependency dev UI: serves index.html and proxies /api/* to the gateway (avoids CORS).
// Usage: GATEWAY=http://localhost:80 PORT=3000 node server.js
const http = require("http");
const fs = require("fs");
const path = require("path");

const GATEWAY = process.env.GATEWAY || "http://localhost:80";
const PORT = process.env.PORT || 3000;

http
  .createServer(async (req, res) => {
    if (req.url.startsWith("/api/")) {
      const chunks = [];
      for await (const c of req) chunks.push(c);
      const body = Buffer.concat(chunks);
      const headers = { "content-type": req.headers["content-type"] || "application/json" };
      if (req.headers.authorization) headers.authorization = req.headers.authorization;
      try {
        const r = await fetch(GATEWAY + req.url, {
          method: req.method,
          headers,
          body: ["GET", "HEAD"].includes(req.method) || !body.length ? undefined : body,
        });
        res.writeHead(r.status, { "content-type": r.headers.get("content-type") || "text/plain" });
        res.end(Buffer.from(await r.arrayBuffer()));
      } catch (e) {
        res.writeHead(502, { "content-type": "text/plain" });
        res.end(`Cannot reach gateway at ${GATEWAY}: ${e.cause?.code || e.message}`);
      }
      return;
    }
    res.writeHead(200, { "content-type": "text/html" });
    res.end(fs.readFileSync(path.join(__dirname, "index.html")));
  })
  .listen(PORT, () => console.log(`UI http://localhost:${PORT}  ->  gateway ${GATEWAY}`));
