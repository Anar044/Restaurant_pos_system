import json
import urllib.error
import urllib.request
from decimal import Decimal

BASE = "http://127.0.0.1:8180"
RESTAURANT_ID = "11111111-1111-1111-1111-111111111111"
DEVICE_ID = "01a0b072-5a20-7a10-add0-4f890c477588"


def call(method, path, token=None, body=None, expected=None):
    payload = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE + path, data=payload, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            raw = response.read().decode("utf-8")
            data = json.loads(raw or "{}")
            status = response.status
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8")
        data = json.loads(raw or "{}")
        status = error.code

    if expected is not None:
        assert status == expected, (status, data)
    elif not 200 <= status < 300:
        raise AssertionError((status, data))
    return data


auth = call(
    "POST",
    "/api/v1/auth/pin",
    body={"restaurantId": RESTAURANT_ID, "pin": "1234"},
)
token = auth["token"]

shift = call(
    "POST",
    "/api/v1/shifts/open",
    token=token,
    body={"deviceId": DEVICE_ID, "openingCash": 100},
)
shift_id = shift["id"]

call(
    "POST",
    f"/api/v1/shifts/{shift_id}/cash-transactions",
    token=token,
    body={"type": "DEPOSIT", "amount": 20, "reason": "CI change fund"},
)
call(
    "POST",
    f"/api/v1/shifts/{shift_id}/cash-transactions",
    token=token,
    body={"type": "WITHDRAWAL", "amount": 5, "reason": "CI safe drop"},
)

x_report = call(
    "GET",
    f"/api/v1/shifts/{shift_id}/report",
    token=token,
)
assert x_report["reportType"] == "X", x_report
assert Decimal(str(x_report["openingCash"])) == Decimal("100"), x_report
assert Decimal(str(x_report["deposits"])) == Decimal("20"), x_report
assert Decimal(str(x_report["withdrawals"])) == Decimal("5"), x_report
assert Decimal(str(x_report["expectedCash"])) == Decimal("115"), x_report
assert len(x_report["cashTransactions"]) == 2, x_report

missing_reason = call(
    "POST",
    f"/api/v1/shifts/{shift_id}/close",
    token=token,
    body={"closingCash": 114},
    expected=400,
)
assert missing_reason["code"] == "SHIFT_DIFFERENCE_REASON_REQUIRED", missing_reason
assert Decimal(str(missing_reason["difference"])) == Decimal("-1"), missing_reason

closed = call(
    "POST",
    f"/api/v1/shifts/{shift_id}/close",
    token=token,
    body={
        "closingCash": 114,
        "reason": "CI counted one manat short",
    },
)
assert Decimal(str(closed["expectedCash"])) == Decimal("115"), closed
assert Decimal(str(closed["difference"])) == Decimal("-1"), closed
z = closed["report"]
assert z["reportType"] == "Z", z
assert z["status"] == "CLOSED", z
assert Decimal(str(z["expectedCash"])) == Decimal("115"), z
assert Decimal(str(z["closingCash"])) == Decimal("114"), z
assert Decimal(str(z["cashDifference"])) == Decimal("-1"), z
assert z["closingNote"] == "CI counted one manat short", z

finance = call(
    "GET",
    f"/api/v1/backoffice/finance?shiftId={shift_id}&take=50",
    token=token,
)
selected = finance["selectedShiftReport"]
assert selected["shiftId"] == shift_id, selected
assert selected["reportType"] == "Z", selected
assert Decimal(str(selected["expectedCash"])) == Decimal("115"), selected
assert Decimal(str(selected["cashDifference"])) == Decimal("-1"), selected
assert selected["closingNote"] == "CI counted one manat short", selected
assert len(selected["cashTransactions"]) == 2, selected

stored_shift = next(x for x in finance["shifts"] if x["id"] == shift_id)
assert Decimal(str(stored_shift["expectedCashAtClose"])) == Decimal("115"), stored_shift
assert Decimal(str(stored_shift["cashDifference"])) == Decimal("-1"), stored_shift
assert stored_shift["closingNote"] == "CI counted one manat short", stored_shift

print("Shift cash reconciliation runtime smoke passed.")
