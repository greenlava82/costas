#!/usr/bin/env python3
# Costas - a station-centric FT8/FT4 client.
# Copyright (C) 2026 Costas contributors
#
# This program is free software: you can redistribute it and/or modify it under the terms of the
# GNU General Public License as published by the Free Software Foundation, either version 3 of the
# License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
# see the GNU General Public License in LICENSE for details.

# A stand-in rigctld on a CAT link that RF is disturbing: after the start-up read, the next N frequency reads answer
# RPRT -5 (Hamlib's timeout, as when a garbled CI-V reply outlasts its retries), then reads work again. "l ALC" can be
# made to answer late. Settings come in -C as comma-separated key=value: log=<file> (each run appends "start" and every
# command), badreads=<N>, slowalc=1.
import socket, sys, threading, time

args = sys.argv[1:]
port = int(args[args.index("-t") + 1])
conf = dict(kv.split("=", 1) for kv in args[args.index("-C") + 1].split(","))
log = conf["log"]
bad = int(conf.get("badreads", "0"))
slow_alc = conf.get("slowalc") == "1"
lock = threading.Lock()
reads = [0]

with open(log, "a") as f:
    f.write("start\n")

srv = socket.socket()
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("127.0.0.1", port))
srv.listen(4)

def serve(conn):
    buf = conn.makefile("rw", newline="\n")
    try:
        for line in buf:
            cmd = line.strip().lstrip("+")
            with lock:
                with open(log, "a") as f:
                    f.write(cmd + "\n")
                if cmd == "f":
                    reads[0] += 1
                    n = reads[0]
            if cmd == "f":
                if 1 < n <= 1 + bad:
                    buf.write("get_freq:\nRPRT -5\n")
                else:
                    buf.write("get_freq:\nFrequency: 14074000\nRPRT 0\n")
            elif cmd.startswith("l "):
                if cmd == "l ALC" and slow_alc:
                    time.sleep(3)
                buf.write("get_level: %s\n0.1\nRPRT 0\n" % cmd[2:])
            else:
                buf.write("%s\nRPRT 0\n" % cmd)
            buf.flush()
    except (OSError, ValueError):
        pass

while True:
    c, _ = srv.accept()
    threading.Thread(target=serve, args=(c,), daemon=True).start()
