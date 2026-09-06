/**
 * Mock Patreon REST API Server
 * Emulates the Patreon API v2 endpoint for the Timberborn Patreon Beaver Names mod.
 *
 * Endpoint: http://localhost:3000/api/patreons
 */

const http = require('http');

const PORT = 3000;

// 30 Patreon supporter names
const PATREON_NAMES = [
  "Alice Walker",
  "Bob Johnson",
  "Charlie Brown",
  "Diana Prince",
  "Erik Lindqvist",
  "Fatima Al-Zahra",
  "Gustav Vasa",
  "Hana Takahashi",
  "Ivan Petrov",
  "Julia Roberts",
  "Karl Johansson",
  "Lena Nygård",
  "Marcus Aurelius",
  "Nina Simone",
  "Oscar Wilde",
  "Penny Lane",
  "Quinn Fabray",
  "Robin Hood",
  "Sophia Loren",
  "Thomas Edison",
  "Uma Thurman",
  "Victor Stone",
  "Wendy Darling",
  "Xavier Charles",
  "Yasmine Bleeth",
  "Zachary Levi",
  "Arthur Dent",
  "Beatrice Portinari",
  "Cedric Diggory",
  "Dorothy Gale"
];

// Patreon API v2 schema response payload
const responsePayload = {
  data: PATREON_NAMES.map((name, index) => ({
    id: String(index + 1),
    type: "member",
    attributes: {
      full_name: name
    }
  }))
};

const server = http.createServer((req, res) => {
  const timestamp = new Date().toISOString();
  console.log(`[${timestamp}] ${req.method} ${req.url} - User-Agent: ${req.headers['user-agent'] || 'Unknown'}`);

  // CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  if (req.url === '/api/patreons' && req.method === 'GET') {
    const json = JSON.stringify(responsePayload, null, 2);
    res.writeHead(200, {
      'Content-Type': 'application/json; charset=utf-8',
      'Content-Length': Buffer.byteLength(json)
    });
    res.end(json);
    return;
  }

  if (req.url === '/' && req.method === 'GET') {
    res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(`Patreon Mock API is running.\nEndpoint: http://localhost:${PORT}/api/patreons (${PATREON_NAMES.length} supporters configured)`);
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({ error: "Not Found", message: "Use GET /api/patreons" }));
});

server.listen(PORT, 'localhost', () => {
  console.log(`===================================================`);
  console.log(`🚀 Patreon Mock API Server running at:`);
  console.log(`   http://localhost:${PORT}/api/patreons`);
  console.log(`   Loaded ${PATREON_NAMES.length} supporter names.`);
  console.log(`===================================================`);
});
