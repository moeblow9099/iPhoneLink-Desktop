# Packet Comparison Report

The app provides Compare CONNECT Profiles for live inspection.

## Profile A

- Name: MAP Target, MaxPacket 65535
- Basis: MAP 6.4.1 Table 6.9 mandatory Target header and variable maximum packet length.
- Expected validation: pass.

## Profile B

- Name: MAP Target, MaxPacket 8192
- Basis: OBEX CONNECT negotiates maximum packet length; OBEX notes larger packet sizes such as 8 KB can improve transfers.
- Expected validation: pass.

## Profile C

- Name: MAP Target, MaxPacket 1024
- Basis: OBEX CONNECT negotiates maximum packet length; 1024 remains above the 255-byte default/minimum guidance.
- Expected validation: pass.

## Profile D

- Name: MAP Target + MapSupportedFeatures, MaxPacket 8192
- Basis: MAP 6.4.1 C.1 conditionally allows/mandates Application Parameters MapSupportedFeatures when the MSE SDP record advertises bit 19.
- Expected validation: pass.

## Profile E

- Name: MAP Target + MapSupportedFeatures, MaxPacket 65535
- Basis: Same as Profile D with the current max packet size.
- Expected validation: pass.

## Not sent automatically

- Header ordering with Application Parameters before Target is not sent because OBEX 2.2.7 requires Target first when used.
- Initial CONNECT with Connection ID is not sent because OBEX 2.2.7 forbids Target and Connection ID in the same request.
- MNS Target on the MAS RFCOMM channel is not sent because MAP 6.4.2 is for Message Access Service, while MNS setup is separate and role-reversed.

Sources:

- Bluetooth MAP 1.4.3 Section 6.4.1 Table 6.9 and C.1: https://www.bluetooth.com/wp-content/uploads/2025/04/MAP_v1.4.3_showing_changes_from_MAP_v1.4.2.pdf
- OBEX 1.5 Section 2.2.7 Target and Connection ID rules: https://btprodspecificationrefs.blob.core.windows.net/ext-ref/IrDA/OBEX15.pdf
