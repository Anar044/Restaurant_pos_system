import json
import urllib.error
import urllib.request
from decimal import Decimal

BASE = "http://127.0.0.1:8180"
RESTAURANT_ID = "11111111-1111-1111-1111-111111111111"


def call(method, path, token=None, body=None, expected=None):
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            raw = response.read().decode("utf-8")
            payload = json.loads(raw or "{}")
            status = response.status
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8")
        payload = json.loads(raw or "{}")
        status = error.code

    if expected is not None:
        assert status == expected, (status, payload)
    elif not 200 <= status < 300:
        raise AssertionError((status, payload))
    return payload


auth = call(
    "POST",
    "/api/v1/auth/pin",
    body={"restaurantId": RESTAURANT_ID, "pin": "1234"},
)
token = auth["token"]
setup = call("GET", "/api/v1/backoffice/adjustments", token=token)

role = next(x for x in setup["roles"] if x["canApplyAdjustments"])
products = {x["name"]: x for x in setup["products"]}
categories = {x["name"]: x for x in setup["categories"]}

burger = products["Burger"]
pasta = products["Pasta"]
cola = products["Cola"]
water = products["Water"]
drinks = categories["Drinks"]


def preset_payload(
    name,
    kind,
    mode,
    value,
    priority,
    *,
    application="MANUAL",
    product_ids=None,
    category_ids=None,
    can_stack=True,
):
    return {
        "name": name,
        "type": kind,
        "mode": mode,
        "scope": "ORDER",
        "applicationMode": application,
        "timeBasis": "ITEM_ADDED_AT",
        "value": value,
        "priority": priority,
        "canStack": can_stack,
        "weekdayMask": 127,
        "startMinute": None,
        "endMinute": None,
        "requireComment": False,
        "isActive": True,
        "roleIds": [role["id"]] if application == "MANUAL" else [],
        "productIds": product_ids or [],
        "categoryIds": category_ids or [],
    }


burger_discount = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=preset_payload(
        "CI Burger 10%",
        "DISCOUNT",
        "PERCENT",
        10,
        10,
        product_ids=[burger["id"]],
    ),
)

drinks_surcharge = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=preset_payload(
        "CI Drinks +50%",
        "SERVICE_CHARGE",
        "PERCENT",
        50,
        20,
        category_ids=[drinks["id"]],
    ),
)


def create_order():
    return call(
        "POST",
        "/api/v1/orders",
        token=token,
        body={"guestCount": 1},
    )


def add(order_id, product_id, quantity=1):
    return call(
        "POST",
        f"/api/v1/orders/{order_id}/items",
        token=token,
        body={
            "productId": product_id,
            "quantity": quantity,
            "guestNumber": 1,
        },
    )


order = create_order()
order_id = order["id"]
order = add(order_id, burger["id"])
order = add(order_id, cola["id"])
order = call(
    "PUT",
    f"/api/v1/orders/{order_id}/discount",
    token=token,
    body={"presetId": burger_discount["id"]},
)
order = call(
    "PUT",
    f"/api/v1/orders/{order_id}/service-charge",
    token=token,
    body={"presetId": drinks_surcharge["id"]},
)

subtotal = Decimal(str(order["subtotal"]))
discount = Decimal(str(order["discountTotal"]))
surcharge = Decimal(str(order["surchargeTotal"]))
total = Decimal(str(order["total"]))

assert subtotal == Decimal("15"), order
assert discount == Decimal("1.2"), order
assert surcharge == Decimal("1.5"), order
assert total == Decimal("15.3"), order

# Change the BackOffice preset after it has already been applied.
updated_payload = preset_payload(
    "CI Burger 10%",
    "DISCOUNT",
    "PERCENT",
    50,
    10,
    product_ids=[burger["id"]],
)
call(
    "PUT",
    f"/api/v1/backoffice/adjustments/{burger_discount['id']}",
    token=token,
    body=updated_payload,
)

# Force another order recalculation by adding a non-target Food item.
order = add(order_id, pasta["id"])
assert Decimal(str(order["discountTotal"])) == Decimal("1.2"), order
applied = next(
    x for x in order["adjustments"]
    if x["presetId"] == burger_discount["id"]
)
assert Decimal(str(applied["value"])) == Decimal("10"), applied

# Automatic product rule: Water -10%.
auto_water = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=preset_payload(
        "CI Auto Water 10%",
        "DISCOUNT",
        "PERCENT",
        10,
        5,
        application="AUTOMATIC",
        product_ids=[water["id"]],
    ),
)

auto_order = create_order()
auto_order = add(auto_order["id"], water["id"])
auto_adjustment = next(
    x for x in auto_order["adjustments"]
    if x["presetId"] == auto_water["id"]
)
assert auto_adjustment["applicationMode"] == "AUTOMATIC", auto_adjustment
assert Decimal(str(auto_order["subtotal"])) == Decimal("2"), auto_order
assert Decimal(str(auto_order["discountTotal"])) == Decimal("0.2"), auto_order
assert Decimal(str(auto_order["total"])) == Decimal("1.8"), auto_order

# Priority math, discount first: 12 - 10 = 2, then +10% = 2.20.
discount_first = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=preset_payload(
        "CI Priority Discount",
        "DISCOUNT",
        "FIXED",
        10,
        100,
    ),
)
service_second = call(
    "POST",
    "/api/v1/backoffice/adjustments",
    token=token,
    body=preset_payload(
        "CI Priority Service",
        "SERVICE_CHARGE",
        "PERCENT",
        10,
        200,
    ),
)

priority_order = create_order()
priority_order = add(priority_order["id"], burger["id"])
priority_order = call(
    "PUT",
    f"/api/v1/orders/{priority_order['id']}/discount",
    token=token,
    body={"presetId": discount_first["id"]},
)
priority_order = call(
    "PUT",
    f"/api/v1/orders/{priority_order['id']}/service-charge",
    token=token,
    body={"presetId": service_second["id"]},
)
assert Decimal(str(priority_order["total"])) == Decimal("2.2"), priority_order

print("Pricing engine runtime smoke passed.")
