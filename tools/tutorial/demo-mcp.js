// demo-mcp.js <server>: a pretend MCP server for the tutorial screenshots. It only lists believable tools; calling one
// answers with a short made-up text and touches nothing.
const readline = require('readline');
const name = process.argv[2] || 'demo';
const s = (props, required = []) => ({ type: 'object', properties: props, required });
const str = d => ({ type: 'string', description: d });
const ro = { readOnlyHint: true };
const catalog = {
  github: [
    ['search_repositories', 'Searches GitHub repositories.', s({ query: str('Search terms') }, ['query']), ro],
    ['list_issues', 'Lists the issues of a repository.', s({ owner: str('Owner'), repo: str('Repository'), state: { type: 'string', enum: ['open', 'closed', 'all'] } }, ['owner', 'repo']), ro],
    ['create_issue', 'Opens a new issue.', s({ owner: str('Owner'), repo: str('Repository'), title: str('Title'), body: str('Text') }, ['owner', 'repo', 'title'])],
    ['list_pull_requests', 'Lists the pull requests of a repository.', s({ owner: str('Owner'), repo: str('Repository') }, ['owner', 'repo']), ro],
    ['get_file_contents', 'Reads a file from a repository.', s({ owner: str('Owner'), repo: str('Repository'), path: str('Path') }, ['owner', 'repo', 'path']), ro],
  ],
  calendar: [
    ['list_events', 'Lists the calendar events between two dates.', s({ from: str('Start date'), to: str('End date') }), ro],
    ['create_event', 'Adds an event to the calendar.', s({ title: str('Title'), start: str('Start, ISO 8601'), minutes: { type: 'integer', description: 'Length' } }, ['title', 'start'])],
    ['delete_event', 'Deletes an event.', s({ id: str('Event id') }, ['id'])],
  ],
  spotify: [
    ['search', 'Searches songs, albums, artists and playlists.', s({ query: str('What to look for'), type: { type: 'string', enum: ['track', 'album', 'artist', 'playlist'] } }, ['query']), ro],
    ['play', 'Plays a song, album or playlist.', s({ uri: str('Spotify URI') })],
    ['pause', 'Pauses the playback.', s({})],
    ['next_track', 'Skips to the next song.', s({})],
    ['add_to_queue', 'Adds a song to the queue.', s({ uri: str('Spotify URI') }, ['uri'])],
    ['now_playing', 'Says what is playing.', s({}), ro],
  ],
  filesystem: [
    ['read_text_file', 'Reads a text file.', s({ path: str('Path') }, ['path']), ro],
    ['list_directory', 'Lists a folder.', s({ path: str('Path') }, ['path']), ro],
    ['write_file', 'Writes a file.', s({ path: str('Path'), content: str('Text') }, ['path', 'content'])],
    ['search_files', 'Finds files by name.', s({ path: str('Folder'), pattern: str('Pattern') }, ['path', 'pattern']), ro],
  ],
  homeassistant: [
    ['turn_on', 'Turns on a light or a switch.', s({ entity_id: str('Entity') }, ['entity_id'])],
    ['turn_off', 'Turns off a light or a switch.', s({ entity_id: str('Entity') }, ['entity_id'])],
    ['get_state', 'Reads the state of an entity.', s({ entity_id: str('Entity') }, ['entity_id']), ro],
  ],
};
const tools = (catalog[name] || []).map(([n, d, schema, annotations]) => ({ name: n, description: d, inputSchema: schema, ...(annotations ? { annotations } : {}) }));
const send = m => process.stdout.write(JSON.stringify(m) + '\n');
readline.createInterface({ input: process.stdin }).on('line', line => {
  let r; try { r = JSON.parse(line); } catch { return; }
  switch (r.method) {
    case 'initialize': return send({ jsonrpc: '2.0', id: r.id, result: { protocolVersion: r.params.protocolVersion, serverInfo: { name, version: '1.2.0' }, capabilities: { tools: {} } } });
    case 'tools/list': return send({ jsonrpc: '2.0', id: r.id, result: { tools } });
    case 'tools/call': return send({ jsonrpc: '2.0', id: r.id, result: { content: [{ type: 'text', text: 'Done.' }] } });
    case 'ping': return send({ jsonrpc: '2.0', id: r.id, result: {} });
    default: if (r.id !== undefined) send({ jsonrpc: '2.0', id: r.id, error: { code: -32601, message: 'Method not found: ' + r.method } });
  }
});
