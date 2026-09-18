# Protocol Timeline

## Expected analyzer sequence

1. Refresh Paired Devices.
2. Select iPhone.
3. List All RFCOMM.
4. Compare CONNECT Profiles.
5. Run CONNECT Profiles.
6. Export Packet Log.
7. Export Session Report.

## Per-attempt timeline captured by the app

1. RFCOMM connect starts.
2. RFCOMM connect completes or fails with HRESULT/exception.
3. CONNECT packet validates.
4. TX timestamp is logged.
5. Every TX byte is logged.
6. Receive loop starts.
7. Every RX byte is logged as it arrives.
8. If three bytes arrive, the OBEX response length is decoded.
9. If OBEX CONNECT succeeds, RFCOMM, OBEX, and MAP state remain connected.
10. List MAP Folders, List Messages, and Read Selected Message reuse the existing socket and Connection ID.
11. If zero bytes arrive and Windows aborts, HRESULT/socket state is logged and the session is marked disconnected.
12. Session disconnect is logged only for user Disconnect, remote disconnect, timeout, or fatal protocol error.

## Interpretation rule

- Packet validation failure means local packet formatting must be fixed before protocol conclusions.
- Validation pass plus zero response bytes plus immediate remote close means the iPhone accepted RFCOMM but rejected or terminated the OBEX session before returning an OBEX response.
- Repeated rejection across all standards-based profiles supports Apple policy/restriction or unsupported third-party MAP client behavior, unless public documentation identifies another required negotiation step.
