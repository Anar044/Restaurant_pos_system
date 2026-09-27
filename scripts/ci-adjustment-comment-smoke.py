import json
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8180"
RESTAURANT_ID = "11111111-1111-1111-1111-111111111111"


def call(method, path, token=None, body=None, expected=None):
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            payload = json.loads(response.read().decode("utf-8") or "{}")
            status = response.status
    except urllib.error.HTTPError as error:
        payload = json.loads(error.read().decode("utf-8") or "{}")
        status = error.code

    if expected is not None:
        assert status == expected, (status, payload)
    elif status < 200 or status >= 300:
        raise AssertionError((status, payload))
    return status, payload


_, auth = call(
    "POST",
    "/api/v1/auth/pin",
    body={"restaurantId": RESTAURANT_ID, "pin": "1234"},
)
token = auth["token"]

_, setup = call("GET", "/api/v1/backoffice/adjustments", token=token)
role_id = next(role["id"] for role in setup["roles"] if role["canApplyAdjustments"])

_, required = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body={
        "name": "CI Required Comment 10%",
        "type": "DISCOUNT",
        "mode": "PERCENT",
        "scope": "ORDER",
        "value": 10,
        "requireComment": True,
        "isActive": True,
        "roleIds": [role_id],
    },
)
assert required["requireComment"] is True

_, optional = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body={
        "name": "CI Optional Service 5%",
        "type": "SERVICE_CHARGE",
        "mode": "PERCENT",
        "scope": "ORDER",
        "value": 5,
        "requireComment": False,
        "isActive": True,
        "roleIds": [role_id],
    },
)
assert optional["requireComment"] is False

_, pos_presets = call("GET", "/api/v1/order-adjustment-presets", token=token)
required_pos = next(x for x in pos_presets["presets"] if x["id"] == required["id"])
optional_pos = next(x for x in pos_presets["presets"] if x["id"] == optional["id"])
assert required_pos["requireComment"] is True
assert optional_pos["requireComment"] is False

_, menu = call("GET", "/api/v1/menu", token=token)
product_id = next(product["id"] for category in menu["categories"] for product in category["products"])

_, order = call("POST", "/api/v1/orders", token=token, body={"guestCount": 1})
order_id = order["id"]
call(
    "POST",
    f"/api/v1/orders/{order_id}/items",
    token=token,
    body={"productId": product_id, "quantity": 1, "guestNumber": 1},
)

_, rejected = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": required["id"]},
    expected=400,
)
assert rejected["code"] == "ADJUSTMENT_COMMENT_REQUIRED"

comment = "Постоянный клиент, согласовано владельцем"
_, discounted = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": required["id"], "comment": comment},
)
discount = next(x for x in discounted["adjustments"] if x["type"] == "DISCOUNT")
assert discount["reason"] == comment

_, serviced = call(
    "PUT",
    f"/api/v1/orders/{order_id}/service-charge",
    token=token,
    body={"presetId": optional["id"]},
)
service = next(x for x in serviced["adjustments"] if x["type"] == "SERVICE_CHARGE")
assert service["reason"] is None

print("Adjustment comment security smoke passed.")
