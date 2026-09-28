import json
import urllib.error
import urllib.request
from decimal import Decimal

BASE = "http://127.0.0.1:8180"
RESTAURANT_ID = "11111111-1111-1111-1111-111111111111"


def call(method, path, token=None, body=None, expected=None):
    payload = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(BASE + path, data=payload, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
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

setup = call("GET", "/api/v1/backoffice/adjustments", token=token)
role = next(x for x in setup["roles"] if x["canApplyAdjustments"])
burger = next(x for x in setup["products"] if x["name"] == "Burger")

base_rule = {
    "name": "CI POS selected Burger 50%",
    "type": "DISCOUNT",
    "mode": "PERCENT",
    "scope": "ORDER",
    "applicationMode": "MANUAL",
    "timeBasis": "ITEM_ADDED_AT",
    "targetMode": "POS_SELECTION",
    "value": 50,
    "priority": 10,
    "canStack": True,
    "weekdayMask": 127,
    "startMinute": None,
    "endMinute": None,
    "requireComment": False,
    "isActive": True,
    "roleIds": [role["id"]],
    "productIds": [],
    "categoryIds": [],
}
preset = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=base_rule,
)
assert preset["targetMode"] == "POS_SELECTION", preset

# Automatic POS selection must be rejected.
invalid_auto = dict(base_rule)
invalid_auto["name"] = "CI invalid automatic selection"
invalid_auto["applicationMode"] = "AUTOMATIC"
invalid_auto["roleIds"] = []
call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=invalid_auto,
    expected=400,
)

# PRESET_SELECTION without selected products/categories must be rejected.
invalid_fixed = dict(base_rule)
invalid_fixed["name"] = "CI invalid empty preset selection"
invalid_fixed["targetMode"] = "PRESET_SELECTION"
call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=invalid_fixed,
    expected=400,
)

order = call(
    "POST",
    "/api/v1/orders",
    token=token,
    body={"guestCount": 1},
)
order_id = order["id"]

first_response = call(
    "POST",
    f"/api/v1/orders/{order_id}/items",
    token=token,
    body={"productId": burger["id"], "quantity": 1, "guestNumber": 1},
)
first_id = first_response["items"][0]["id"]

second_response = call(
    "POST",
    f"/api/v1/orders/{order_id}/items",
    token=token,
    body={"productId": burger["id"], "quantity": 1, "guestNumber": 1},
)
items = second_response["items"]
assert len(items) == 2, items
second_id = next(x["id"] for x in items if x["id"] != first_id)

available = call(
    "GET",
    f"/api/v1/order-adjustment-presets?orderId={order_id}",
    token=token,
)
available_rule = next(x for x in available["presets"] if x["id"] == preset["id"])
assert available_rule["targetMode"] == "POS_SELECTION", available_rule
assert set(available_rule["eligibleOrderItemIds"]) == {first_id, second_id}, available_rule

# No selected item -> reject.
missing = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": preset["id"]},
    expected=400,
)
assert missing["code"] == "ORDER_ITEMS_REQUIRED", missing

discounted = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": preset["id"], "orderItemIds": [first_id]},
)
first = next(x for x in discounted["items"] if x["id"] == first_id)
subtotal = Decimal(str(discounted["subtotal"]))
expected_discount = (Decimal(str(first["lineTotal"])) * Decimal("0.50")).quantize(Decimal("0.0001"))
assert Decimal(str(discounted["discountTotal"])) == expected_discount, discounted

applied = next(x for x in discounted["adjustments"] if x["presetId"] == preset["id"])
assert applied["targetMode"] == "POS_SELECTION", applied
assert applied["orderItemIdsSnapshot"] == [first_id], applied

# A new identical dish must not inherit the already applied selection.
third_response = call(
    "POST",
    f"/api/v1/orders/{order_id}/items",
    token=token,
    body={"productId": burger["id"], "quantity": 1, "guestNumber": 1},
)
assert len(third_response["items"]) == 3, third_response
assert Decimal(str(third_response["discountTotal"])) == expected_discount, third_response

# Reapplying the same preset replaces its selected lines, not adds a duplicate adjustment.
reselected = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": preset["id"], "orderItemIds": [second_id]},
)
matching = [x for x in reselected["adjustments"] if x["presetId"] == preset["id"]]
assert len(matching) == 1, matching
assert matching[0]["orderItemIdsSnapshot"] == [second_id], matching[0]

# Foreign order item IDs must be rejected.
other = call(
    "POST",
    "/api/v1/orders",
    token=token,
    body={"guestCount": 1},
)
other = call(
    "POST",
    f"/api/v1/orders/{other['id']}/items",
    token=token,
    body={"productId": burger["id"], "quantity": 1, "guestNumber": 1},
)
foreign_id = other["items"][0]["id"]
call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": preset["id"], "orderItemIds": [foreign_id]},
    expected=400,
)

print("POS-selected pricing item smoke passed.")
