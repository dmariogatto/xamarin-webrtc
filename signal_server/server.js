"use strict";

let WebSocketServer = require('ws').Server;
let port = 8080;
let wsServer = new WebSocketServer({ port: port });
const ip = require('ip');
console.log('WebSocket Broadcaster Started - ' + 'ws://' + ip.address() + ':' + port);

wsServer.on('connection', function (ws) 
{
    console.log('## WebSocket Connection ##');

    ws.on('message', function (message) 
    {
        console.log('## Message Recieved ##');
        const json = JSON.parse(message.toString());
        console.log('\t' + message.toString());

        wsServer.clients.forEach(function each(client) {
            if (isSame(ws, client))
            {
                console.log('## Skipping Sender ##');
            }
            else 
            {
                client.send(message);
            }
        });
    });

});

function isSame(ws1, ws2) {
    return (ws1 === ws2);
}