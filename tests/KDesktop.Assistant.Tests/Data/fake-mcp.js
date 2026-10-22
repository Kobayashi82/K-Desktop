// A pretend MCP server for McpTests (stdio, JSON-RPC by hand, like mcp-communicator-telegram): it also writes traces
// to stdout, which a client must leave out, and to stderr. Tools: echo (read-only; says back its text and the secret
// it got in FAKE_SECRET), fail (an error result) and slow (never answers).
const readline = require('readline');
console.log('Fake MCP server starting (a trace on stdout)');
console.error('fake-mcp: started, pid ' + process.pid);
const send = message => process.stdout.write(JSON.stringify(message) + '\n');
const tools = [
  { name: 'echo', description: 'Says back the text it is given.', inputSchema: { type: 'object', properties: { text: { type: 'string' } }, required: ['text'] },
    annotations: { readOnlyHint: true } },
  { name: 'fail', description: 'Always fails.', inputSchema: { type: 'object' } },
  { name: 'slow', description: 'Never answers.', inputSchema: { type: 'object', properties: {} } },
];
readline.createInterface({ input: process.stdin }).on('line', line => {
  let request;
  try { request = JSON.parse(line); } catch { return; }
  console.log('Received request: ' + request.method);
  switch (request.method) {
    case 'initialize':
      return send({ jsonrpc: '2.0', id: request.id, result: { protocolVersion: request.params.protocolVersion,
        serverInfo: { name: 'fake-mcp', version: '1.0.0' }, capabilities: { tools: {} } } });
    case 'tools/list':
      return send({ jsonrpc: '2.0', id: request.id, result: { tools } });
    case 'tools/call': {
      const { name, arguments: args } = request.params;
      if (name === 'echo')
        return send({ jsonrpc: '2.0', id: request.id, result: { content: [{ type: 'text', text: `echo: ${args.text} (secret ${process.env.FAKE_SECRET})` }] } });
      if (name === 'fail')
        return send({ jsonrpc: '2.0', id: request.id, result: { content: [{ type: 'text', text: 'it went wrong' }], isError: true } });
      return; // slow: never
    }
    case 'ping':
      return send({ jsonrpc: '2.0', id: request.id, result: {} });
    default:
      // server/discover (the 2026 revision) and the rest: unknown, as an older server says.
      if (request.id !== undefined) send({ jsonrpc: '2.0', id: request.id, error: { code: -32601, message: 'Method not found: ' + request.method } });
  }
});
