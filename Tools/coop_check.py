#!/usr/bin/env python3
"""Runs a real co-op session on this machine and checks every player saw the same game.

One headless copy of the desktop build hosts; the rest join it over UDP on
localhost. Each copy is driven by the in-game bot (see BotDriver.cs), plays
through a pizza delivery, a store robbery, an escape, an arrest, a grab
conflict, a mid-carry disconnect and the host leaving, and writes a report.
This script compares the reports.

    python3 Tools/coop_check.py --players 2
    python3 Tools/coop_check.py --players 4

Build the desktop player first:
    ./run_checks.sh mac
"""
import argparse
import glob
import json
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIZZA_PAY = 60 + 25      # payout plus the speed tip; bots are fast
BUNDLE = 45
PISTOL = 110
DEV_MONEY = 200
FINE_LEVEL_2 = 30 + 30 * 2


def find_binary(app):
    candidates = [p for p in glob.glob(os.path.join(app, "Contents", "MacOS", "*")) if os.path.isfile(p)]
    if not candidates:
        sys.exit("No executable in %s. Run ./run_checks.sh mac first." % app)
    return candidates[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--players", type=int, default=2)
    parser.add_argument("--app", default=os.path.join(ROOT, "Builds", "Mac", "SmallTownBigTrouble.app"))
    parser.add_argument("--port", type=int, default=7791)
    parser.add_argument("--timeout", type=int, default=480)
    parser.add_argument("--scenario", default="coop")
    args = parser.parse_args()

    binary = find_binary(args.app)
    out = os.path.join(ROOT, "Logs", "coop", "%dp" % args.players)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)

    processes = []
    for index in range(args.players):
        role = "host" if index == 0 else "client"
        command = [
            binary, "-batchmode", "-nographics",
            "-logFile", os.path.join(out, "player%d.log" % index),
            "-stbt-bot", role,
            "-stbt-index", str(index),
            "-stbt-players", str(args.players),
            "-stbt-port", str(args.port),
            "-stbt-join", "127.0.0.1",
            "-stbt-report", os.path.join(out, "report%d.json" % index),
            "-stbt-scenario", args.scenario,
        ]
        processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        time.sleep(0.5 if index else 2.0)

    deadline = time.time() + args.timeout
    while time.time() < deadline and any(p.poll() is None for p in processes):
        time.sleep(1.0)
    timed_out = [i for i, p in enumerate(processes) if p.poll() is None]
    for p in processes:
        if p.poll() is None:
            p.kill()

    if args.scenario != "coop":
        for index in range(args.players):
            path = os.path.join(out, "report%d.json" % index)
            if os.path.exists(path):
                with open(path) as handle:
                    data = json.load(handle)
                print("== %s%d" % (data["role"], data["index"]))
                for line in data["log"] + ["FAIL " + f for f in data["failures"]]:
                    print("   " + line)
        return

    reports = {}
    for index in range(args.players):
        path = os.path.join(out, "report%d.json" % index)
        if os.path.exists(path):
            with open(path) as handle:
                reports[index] = json.load(handle)

    problems = []
    notes = []

    def check(condition, message):
        (notes if condition else problems).append(("ok   " if condition else "FAIL ") + message)

    check(not timed_out, "every copy exited on its own (still running: %s)" % timed_out)
    check(len(reports) == args.players, "every player wrote a report (%d of %d)" % (len(reports), args.players))
    if len(reports) < args.players:
        return finish(problems, notes, out)

    host = reports[0]
    clients = [reports[i] for i in range(1, args.players)]
    everyone = [host] + clients

    for r in everyone:
        tag = "%s%d" % (r["role"], r["index"])
        check(r["finished"], "%s finished its script" % tag)
        for failure in r["failures"]:
            problems.append("FAIL %s: %s" % (tag, failure))
        check(r["playersSeen"] == args.players, "%s saw %d players" % (tag, r["playersSeen"]))

    # --- Job: one order, three contributors, everyone paid the same once.
    for r in everyone:
        check(r["cashAfterJob"] == r["cashStart"] + PIZZA_PAY,
              "%s%d paid $%d for the shared delivery (%d -> %d)" % (r["role"], r["index"], PIZZA_PAY, r["cashStart"], r["cashAfterJob"]))
    check(len({r["orderLabelAtDelivery"] for r in everyone}) == 1, "everyone read the same order terminal text")

    # --- Robbery: same world state everywhere.
    check(len({r["storeLabelAfterRobbery"] for r in everyone}) == 1 and "CLEANED OUT" in host["storeLabelAfterRobbery"],
          "everyone read the same register sign (%s)" % host["storeLabelAfterRobbery"])
    check(len({r["officersPresent"] for r in everyone}) == 1 and host["officersPresent"] >= 3,
          "everyone counted the same officers on duty (%s)" % [r["officersPresent"] for r in everyone])
    check(len({r["cashBundlesSeen"] for r in everyone}) == 1,
          "everyone counted the same cash bundles in the world (%s)" % [r["cashBundlesSeen"] for r in everyone])

    # --- Wanted levels and money: each client's own numbers match what the host holds for it.
    host_after = {}
    for line in host["hostView"]:
        when, client_id, cash, wanted = line.split("|")
        if when == "after robbery":
            host_after[int(client_id)] = (int(cash.split("=")[1]), int(wanted.split("=")[1]))
    for r in everyone:
        mine = host_after.get(r["clientId"])
        tag = "%s%d" % (r["role"], r["index"])
        check(mine is not None and mine == (r["cashAfterRobbery"], r["wantedAfterRobbery"]),
              "%s and the host agree on its cash and wanted level after the robbery (%s vs %s)" %
              (tag, (r["cashAfterRobbery"], r["wantedAfterRobbery"]), mine))

    robber = clients[0]
    check(robber["wantedAfterRobbery"] == 2, "the robber was wanted at level 2")
    check(host["maxWantedSeen"] >= 2, "the host became wanted for taking stolen cash")
    for r in clients[1:]:
        check(r["maxWantedSeen"] == 0, "bystander client%d was never wanted" % r["index"])

    # --- Outcomes.
    check(host["escaped"], "the host escaped by hiding")
    check(host["cashFinal"] == host["cashAfterJob"] + BUNDLE,
          "the host's stolen bundle became $%d on escaping (%d -> %d)" % (BUNDLE, host["cashAfterJob"], host["cashFinal"]))
    check(robber["wasArrested"], "the robber was arrested")
    expected_robber = robber["cashStart"] + PIZZA_PAY + DEV_MONEY - PISTOL - FINE_LEVEL_2
    check(robber["cashFinal"] == expected_robber,
          "the robber paid for the pistol once and was fined once (final $%d, expected $%d)" % (robber["cashFinal"], expected_robber))

    # --- Grab conflict, disconnect, host leaving.
    winners = sum(1 for r in clients if r["wonConflict"])
    check(winners == 1, "exactly one player won the contested grab (%d of %d)" % (winners, len(clients)))
    check(host["playersAfterLeave"] == args.players - 1, "the host noticed the disconnect (%d players left)" % host["playersAfterLeave"])
    check(host["itemsHeldByDeparted"] == 0, "nothing stayed held by the player who left (%d stuck)" % host["itemsHeldByDeparted"])
    for r in clients[1:]:
        check(r["returnedToMenu"], "client%d returned to the menu when the host left" % r["index"])
        check("host" in r["menuMessage"].lower(), "client%d was told why (%r)" % (r["index"], r["menuMessage"]))

    return finish(problems, notes, out)


def finish(problems, notes, out):
    for line in notes:
        print(line)
    for line in problems:
        print(line)
    print()
    print("%d checks passed, %d failed. Reports and logs: %s" % (len(notes), len(problems), out))
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
