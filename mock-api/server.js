/**
 * Mock Patreon REST API Server
 * Strictly implements the Patreon API v2 schema specified in openapi.json.
 *
 * Supported Endpoints:
 * - GET /api/oauth2/v2/campaigns/{campaign_id}/members (Official OpenAPI endpoint)
 * - GET /api/patreons (Convenience alias)
 *
 * Features:
 * - 30 supporters distributed across Bronze, Silver, and Gold tiers
 * - JSON:API format with "data", "included", and "meta" schemas from openapi.json
 * - Bearer token inspection and optional requirement (?requireAuth=true)
 */

const http = require('http');

const PORT = 3000;

// 30 Patreon supporter names with assigned tiers
const SUPPORTERS = [
  // Bronze Tier ($5) - amount_cents: 500
  { name: "Alice Walker", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Bob Johnson", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Charlie Brown", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Diana Prince", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Erik Lindqvist", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Fatima Al-Zahra", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Gustav Vasa", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Hana Takahashi", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Ivan Petrov", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },
  { name: "Julia Roberts", tier: "Bronze", tierId: "tier-bronze", amount_cents: 500 },

  // Silver Tier ($10) - amount_cents: 1000
  { name: "Karl Johansson", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Lena Nygård", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Marcus Aurelius", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Nina Simone", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Oscar Wilde", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Penny Lane", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Quinn Fabray", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Robin Hood", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Sophia Loren", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },
  { name: "Thomas Edison", tier: "Silver", tierId: "tier-silver", amount_cents: 1000 },

  // Gold Tier ($25) - amount_cents: 2500
  { name: "Uma Thurman", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Victor Stone", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Wendy Darling", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Xavier Charles", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Yasmine Bleeth", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Zachary Levi", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Arthur Dent", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Beatrice Portinari", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Cedric Diggory", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 },
  { name: "Dorothy Gale", tier: "Gold", tierId: "tier-gold", amount_cents: 2500 }
];

// Patreon API v2 JSON:API response payload compliant with openapi.json membersResponse
const responsePayload = {
  data: SUPPORTERS.map((s, index) => ({
    id: String(index + 1),
    type: "member",
    attributes: {
      full_name: s.name,
      patron_status: "active_patron",
      currently_entitled_amount_cents: s.amount_cents,
      tier_title: s.tier
    },
    relationships: {
      currently_entitled_tiers: {
        data: [{ id: s.tierId, type: "tier" }]
      }
    }
  })),
  included: [
    {
      id: "tier-bronze",
      type: "tier",
      attributes: {
        title: "Bronze",
        amount_cents: 500,
        description: "Bronze tier supporter"
      }
    },
    {
      id: "tier-silver",
      type: "tier",
      attributes: {
        title: "Silver",
        amount_cents: 1000,
        description: "Silver tier supporter"
      }
    },
    {
      id: "tier-gold",
      type: "tier",
      attributes: {
        title: "Gold",
        amount_cents: 2500,
        description: "Gold tier supporter"
      }
    }
  ],
  meta: {
    pagination: {
      total: SUPPORTERS.length,
      cursors: null
    }
  }
};

const server = http.createServer((req, res) => {
  const timestamp = new Date().toISOString();
  const authHeader = req.headers['authorization'] || '';
  const urlObj = new URL(req.url, `http://${req.headers.host || 'localhost:' + PORT}`);

  console.log(`[${timestamp}] ${req.method} ${urlObj.pathname}${urlObj.search}`);
  if (authHeader) {
    console.log(`   🔑 Authorization: ${authHeader}`);
  } else {
    console.log(`   ℹ️ No Authorization header provided.`);
  }

  // CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, User-Agent');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // Check auth requirement if requested via query param
  if (urlObj.searchParams.get('requireAuth') === 'true' && !authHeader) {
    console.log(`   ❌ Rejected: Missing required Authorization header.`);
    res.writeHead(401, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({
      errors: [
        {
          code: "401",
          status: "401",
          title: "Unauthorized",
          detail: "The request requires an Authorization header."
        }
      ]
    }));
    return;
  }

  // Match /api/patreons OR /api/oauth2/v2/campaigns/{campaign_id}/members (as defined in openapi.json)
  const isMembersEndpoint =
    urlObj.pathname === '/api/patreons' ||
    /^\/api\/oauth2\/v2\/campaigns\/[^/]+\/members\/?$/.test(urlObj.pathname);

  if (isMembersEndpoint && req.method === 'GET') {
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
      `Patreon OpenAPI Mock Server is running.\n` +
      `Official OpenAPI Endpoint: http://localhost:${PORT}/api/oauth2/v2/campaigns/default/members\n` +
      `Alias Endpoint:            http://localhost:${PORT}/api/patreons\n` +
      `Supporters: ${SUPPORTERS.length} (Bronze: 10, Silver: 10, Gold: 10)\n` +
      `Auth header received: ${authHeader ? 'YES (' + authHeader + ')' : 'NONE'}\n`
    );
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({
    errors: [
      {
        code: "404",
        status: "404",
        title: "Not Found",
        detail: "Endpoint not found. Use GET /api/oauth2/v2/campaigns/{campaign_id}/members or /api/patreons"
      }
    ]
  }));
});

server.listen(PORT, 'localhost', () => {
  console.log(`===================================================`);
  console.log(`🚀 Patreon OpenAPI Mock Server running at:`);
  console.log(`   http://localhost:${PORT}/api/oauth2/v2/campaigns/default/members`);
  console.log(`   http://localhost:${PORT}/api/patreons`);
  console.log(`   Loaded ${SUPPORTERS.length} supporters across Bronze, Silver, Gold tiers.`);
  console.log(`===================================================`);
});
