#!/usr/bin/env python3
"""WP-061 exact N/N soak gate; the older bot gate intentionally only promises four rounds."""
import argparse
import json


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("summary")
    parser.add_argument("--rounds", type=int, required=True)
    args = parser.parse_args()
    try:
        with open(args.summary, encoding="utf-8") as handle:
            summary = json.load(handle)
    except Exception as error:
        print(f"FAIL soak summary unreadable: {error}")
        return 1
    records = summary.get("rounds") or []
    completed = [record for record in records if record.get("completed")]
    ok = (
        summary.get("roundsRequested") == args.rounds
        and summary.get("roundsCompleted") == args.rounds
        and summary.get("deaths") == 0
        and len(records) == args.rounds
        and len(completed) == args.rounds
        and all(str(record.get("outcome", "")).lower() == "completed" for record in records)
    )
    print(
        f"{'PASS' if ok else 'FAIL'} exact soak requested={summary.get('roundsRequested')} "
        f"completed={summary.get('roundsCompleted')} deaths={summary.get('deaths')} records={len(records)}"
    )
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
