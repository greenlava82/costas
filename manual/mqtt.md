# MQTT publishing

Costas can publish what your station is doing to an MQTT broker. Home Assistant, Node-RED, OpenHAB, or a short script
of your own can then react: switch off something that RF upsets while you are on the air, light an ON AIR sign at
the shack door, or notify your phone when a contact is logged.

Publishing goes **one way only**. Costas publishes and never subscribes, so nothing on the broker can change Costas or
make it transmit. It is off until you turn it on, and off while Costas replays recordings (`--simulate`).

!!! note "Untested on the air"
    MQTT publishing has been tested against a test broker but not yet with a real radio and Home Assistant.

## Turning it on

1. Open **Settings › MQTT** and tick **Publish station events to an MQTT broker**.
2. Enter the broker's address and port (1883 for plain, 8883 for TLS), and a user name and password if the broker
   needs them. The password is kept in the system's credential store, never in the settings file.
3. Press **Test connection**. Costas connects, publishes one test message and says how long it took, or what went
   wrong in plain words ("The broker refused the user name or password", "Nothing answered at ... Is the broker
   running?").
4. Press **Save**. The **MQTT** indicator in the bottom bar shows the connection.

With Home Assistant's Mosquitto broker app, use your Home Assistant address and a Home Assistant user.

## The on-air session

Costas publishes two different "on the air" signals:

- **Session** (`session`): starts when Costas is about to transmit (a message queued for a slot, a PSK or RTTY over,
  or Tune) and stays on across every over. It ends after a set time without transmitting (5 minutes by default), and
  at most after a set length (4 hours by default) as a failsafe. Use this one to switch loads: it changes twice per
  operating session, not every 15 seconds.
- **Transmitting** (`tx`): follows PTT exactly. Use it for an ON AIR light, not for switching loads.

The session also ends when the radio connection is lost or Costas closes. Each end carries a reason.

Because nothing comes back from the broker, Costas does not wait for anything before it transmits. The first over of
a session starts as soon as Costas keys, and a load switched by the session turns off a moment later.

## Home Assistant

With **Announce Costas to Home Assistant** ticked (the default), Home Assistant shows a **Costas &lt;callsign&gt;**
device with these entities, without any setup on the Home Assistant side:

| Entity | Type | What it shows |
| --- | --- | --- |
| `binary_sensor.costas_<id>_session_active` | Running | The on-air session |
| `binary_sensor.costas_<id>_transmitting` | Running | PTT keyed |
| `binary_sensor.costas_<id>_rig_connected` | Connectivity | The radio answers CAT |
| `sensor.costas_<id>_state` | Enum | idle, listening, calling_cq, in_qso |
| `sensor.costas_<id>_band` | Text | 20m, 40m, ... |
| `sensor.costas_<id>_frequency` | Frequency | Dial frequency |
| `sensor.costas_<id>_mode` | Text | FT8, FT4, PSK31, RTTY, ... |
| `sensor.costas_<id>_session_end_reason` | Enum | none, idle_timeout, max_length, rig_lost, app_closed |
| `event.costas_<id>_session_event` | Event | session_started, session_ended |
| `event.costas_<id>_qso_event` | Event | qso_logged |

`<id>` is your station id, normally your callsign in lower case (`w7lit`). It is set the first time you turn
publishing on and then kept, so switching station profile does not move the topics or create a second device.

The whole device shows as unavailable when Costas closes, crashes, freezes or loses the network. **Treat unavailable
the same as off** in automations that switch loads, so a crash never leaves something off for good.

A simple automation that turns a plug off for the session and back on afterwards:

```yaml
triggers:
  - trigger: state
    entity_id: binary_sensor.costas_w7lit_session_active
    to: "on"
    id: "on"
  - trigger: state
    entity_id: binary_sensor.costas_w7lit_session_active
    from: "on"
    id: "off"
actions:
  - choose:
      - conditions: [{ condition: trigger, id: "on" }]
        sequence: [{ action: switch.turn_off, target: { entity_id: switch.pump } }]
      - conditions: [{ condition: trigger, id: "off" }]
        sequence:
          - delay: "00:00:15"
          - action: switch.turn_on
            target: { entity_id: switch.pump }
```

The 15 second delay covers an over still finishing when the radio connection drops. Leaving `from: "on"` without a
`to:` makes the plug come back on when the session goes off or unavailable.

## Topics and payloads

Everything is under `costas/<id>/`. Any MQTT client can read these topics; the format is meant to stay stable.

| Topic | Retained | Payload |
| --- | --- | --- |
| `costas/<id>/availability` | Yes | `online`, or `offline` (also the last will, sent by the broker if Costas disappears) |
| `costas/<id>/session` | Yes | JSON, see below |
| `costas/<id>/tx` | Yes | `ON` or `OFF` |
| `costas/<id>/state` | Yes | JSON, see below |
| `costas/<id>/event/session` | No | JSON event |
| `costas/<id>/event/qso` | No | JSON event (if **Publish each logged contact** is ticked) |
| `homeassistant/device/costas_<id>/config` | Yes | Home Assistant discovery (if ticked) |

Session:

```json
{"state":"on","id":"3f9a1c0b7d2e","since":"2026-11-24T18:00:05Z","reason":null,"transmitting":true}
```

`reason` is the last session's end reason while no session is running: `idle_timeout`, `max_length`, `rig_lost` or
`app_closed`.

State:

```json
{"callsign":"W7LIT","rig_connected":true,"state":"calling_cq","band":"20m","frequency":14074000,"mode":"FT8"}
```

Events:

```json
{"event_type":"session_started","session_id":"3f9a1c0b7d2e","reason":null,"ts":"2026-11-24T18:00:05Z"}
{"event_type":"session_ended","session_id":"3f9a1c0b7d2e","reason":"idle_timeout","ts":"2026-11-24T19:42:10Z"}
{"event_type":"qso_logged","call":"K1ABC","band":"20m","mode":"FT8","frequency":14074000,"ts":"2026-11-24T18:03:40Z"}
```

Times are UTC. Frequencies are in hertz.

How Costas keeps the retained topics honest:

- On every connect it publishes its current state first and only then `online`, so a value left over from a crash is
  replaced before anything sees Costas as available.
- It republishes `session`, `tx` and `state` every 30 seconds. Home Assistant's session and transmitting sensors go
  unavailable after 90 seconds without one, which catches a Costas that has frozen.
- While the broker is unreachable Costas keeps operating normally, retries from 1 second up to once a minute, and
  publishes only the latest state when it reconnects. Events from while it was offline are dropped, not sent late.
- It closes cleanly: the session ends with `app_closed`, `tx` goes `OFF`, then `offline`.

## Privacy

Nothing leaves your network unless you point Costas at a broker elsewhere. Contacts are published only with
**Publish each logged contact** ticked. Your grid square is not published.
