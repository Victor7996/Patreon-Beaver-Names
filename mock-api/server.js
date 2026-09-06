/**
 * Mock Patreon REST API Server
 * Emulates the Patreon API v2 endpoint for the Timberborn Patreon Beaver Names mod.
 *
 * Endpoint: http://localhost:3000/api/patreons
 * Supports:
 * - 30 supporters distributed across Bronze, Silver, and Gold tiers
 * - Authorization header inspection and logging (Bearer token)
 * - Optional auth requirement via ?requireAuth=true
 */

const http = require('http');

const PORT = 3000;

// 30 Patreon supporter names with assigned tiers
const SUPPORTERS = [
  // Bronze Tier ($5)
  { name: "Alice Walker", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Bob Johnson", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Charlie Brown", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Diana Prince", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Erik Lindqvist", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Fatima Al-Zahra", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Gustav Vasa", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Hana Takahashi", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Ivan Petrov", tier: "Bronze", tierId: "tier-bronze" },
  { name: "Julia Roberts", tier: "Bronze", tierId: "tier-bronze" },

  // Silver Tier ($10)
  { name: "Karl Johansson", tier: "Silver", tierId: "tier-silver" },
  { name: "Lena Nygård", tier: "Silver", tierId: "tier-silver" },
  { name: "Marcus Aurelius", tier: "Silver", tierId: "tier-silver" },
  { name: "Nina Simone", tier: "Silver", tierId: "tier-silver" },
  { name: "Oscar Wilde", tier: "Silver", tierId: "tier-silver" },
  { name: "Penny Lane", tier: "Silver", tierId: "tier-silver" },
  { name: "Quinn Fabray", tier: "Silver", tierId: "tier-silver" },
  { name: "Robin Hood", tier: "Silver", tierId: "tier-silver" },
  { name: "Sophia Loren", tier: "Silver", tierId: "tier-silver" },
  { name: "Thomas Edison", tier: "Silver", tierId: "tier-silver" },

  // Gold Tier ($25)
  { name: "Uma Thurman", tier: "Gold", tierId: "tier-gold" },
  { name: "Victor Stone", tier: "Gold", tierId: "tier-gold" },
  { name: "Wendy Darling", tier: "Gold", tierId: "tier-gold" },
  { name: "Xavier Charles", tier: "Gold", tierId: "tier-gold" },
  { name: "Yasmine Bleeth", tier: "Gold", tierId: "tier-gold" },
  { name: "Zachary Levi", tier: "Gold", tierId: "tier-gold" },
  { name: "Arthur Dent", tier: "Gold", tierId: "tier-gold" },
  { name: "Beatrice Portinari", tier: "Gold", tierId: "tier-gold" },
  { name: "Cedric Diggory", tier: "Gold", tierId: "tier-gold" },
  { name: "Dorothy Gale", tier: "Gold", tierId: "tier-gold" }
];

// Patreon API v2 response payload
const responsePayload = {
  data: SUPPORTERS.map((s, index) => ({
    id: String(index + 1),
    type: "member",
    attributes: {
      full_name: s.name,
      patron_status: "active_patron",
      tier_title: s.tier
    },
    relationships: {
      currently_entitled_tiers: {
        data: [{ id: s.tierId, type: "tier" }]
      }
    }
  })),
  included: [
    { id: "tier-bronze", type: "tier", attributes: { title: "Bronze", amount_cents: 500 } },
    { id: "tier-silver", type: "tier", attributes: { title: "Silver", amount_cents: 1000 } },
    { id: "tier-gold", type: "tier", attributes: { title: "Gold", amount_cents: 2500 } }
  ]
};

const server = http.createServer((req, res) => {
  const timestamp = new Date().toISOString();
  const authHeader = req.headers['authorization'] || '';
  const urlObj = new URL(req.url, `http://${req.headers.host || 'localhost:' + PORT}`);

  console.log(`[${timestamp}] ${req.method} ${urlObj.pathname}`);
  if (authHeader) {
    console.log(`   🔑 Authorization: ${authHeader}`);
  } else {
    console.log(`   ℹ️ No Authorization header provided.`);
  }

  // CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // Check auth requirement if requested via query param
  if (urlObj.searchParams.get('requireAuth') === 'true' && !authHeader) {
    console.log(`   ❌ Rejected: Missing required Authorization header.`);
    res.writeHead(401, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ error: "Unauthorized", message: "Missing Authorization header." }));
    return;
  }

  if (urlObj.pathname === '/api/patreons' && req.method === 'GET') {
    const json = JSON.stringify(responsePayload, null, 2);
    res.writeHead(200, {
      'Content-Type': 'application/json; charset=utf-8',
      'Content-Length': Buffer.byteLength(json)
    });
    res.end(json);
    return;
  }

  if (urlObj.pathname === '/' && req.method === 'GET') {
    res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end(
      `Patreon Mock API is running.\n` +
      `Endpoint: http://localhost:${PORT}/api/patreons\n` +
      `Configured supporters: ${SUPPORTERS.length} (Bronze: 10, Silver: 10, Gold: 10)\n` +
      `Auth header received: ${authHeader ? 'YES (' + authHeader + ')' : 'NONE'}\n`
    );
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({ error: "Not Found", message: "Use GET /api/patreons" }));
});

server.listen(PORT, 'localhost', () => {
  console.log(`===================================================`);
  console.log(`🚀 Patreon Mock API Server running at:`);
  console.log(`   http://localhost:${PORT}/api/patreons`);
  console.log(`   Loaded ${SUPPORTERS.length} supporters across Bronze, Silver, Gold tiers.`);
  console.log(`===================================================`);
});
