#!/usr/bin/env python3
"""Phase 25: builds the sanitized regression fixtures under fixtures/memberships-and-season-passes/.

Input: one saved copy of the official https://www.sixflags.com/carowinds/memberships-and-season-passes
response (a single bounded GET). Output: small HTML files that keep the page's real data shape — the
Next.js flight chunks (self.__next_f.push([1,"..."])), split across chunks mid-object exactly like the
live page, holding the official product_v2 tier documents with their paymentOptions — plus minimal
rendered tier-card markup. Images, benefit icons, descriptions, disclaimers, CMS internal titles and every
other non-pricing field are removed. Variants (reordered, duplicated, conflicting, missing, unknown tier,
malformed, ...) are derived from the real documents so tests never need the network.

Usage: python3 -I build_fixtures.py <saved-live-page.html> <output-dir>
"""
import copy
import json
import re
import sys

KEEP = ["_id", "_type", "guestFacingTitle", "beforePriceText", "buyNowLink", "currentPrice", "currentPriceModifier",
        "paymentDisplayType", "paymentOptions", "paymentSectionHeader", "savings", "strikethroughPrice", "subtitle",
        "taxesAndFeesDisclosure"]


def extract_products(html):
    chunks = [json.loads(m.group(1)) for m in re.finditer(r'self\.__next_f\.push\(\[1,("(?:[^"\\]|\\.)*")\]\)', html)]
    flight = "".join(chunks)
    decoder = json.JSONDecoder()
    products, seen = [], set()
    for m in re.finditer(r'\{"_id":"([^"]+)","_type":"product_v2"', flight):
        if m.group(1) in seen:
            continue
        seen.add(m.group(1))
        obj, _ = decoder.raw_decode(flight, m.start())
        slim = {k: obj[k] for k in KEEP if k in obj}
        if isinstance(slim.get("buyNowLink"), dict):
            slim["buyNowLink"] = {"keyword": slim["buyNowLink"].get("keyword")}
        products.append(slim)
    return products


def card_markup(product):
    options = "".join(
        '<button type="button" data-ab="productv2-card-payment-option"><span>{}</span><span>{}</span></button>'.format(
            o.get("optionTitle") or "", (o.get("price") or o.get("lockedText") or "").replace("$$", "$"))
        for o in product.get("paymentOptions") or [])
    return ('<div data-ab="productv2-card" data-ab-product="{}"><h3 data-ab="productv2-card-title">{}</h3>'
            '<p data-ab="productv2-card-payment-header">Choose your way</p>{}</div>').format(
        product.get("_id", ""), product.get("guestFacingTitle", ""), options)


def page(products, *, cards=None, raw_flight=None, with_flight=True):
    cards = products if cards is None else cards
    flight = raw_flight
    if flight is None:
        tree = ["$", "div", None, {"className": "tabpanel", "children": [{"products": products}]}]
        flight = '0:{"P":null,"b":"fixture"}\n5:' + json.dumps(tree, ensure_ascii=False, separators=(",", ":")) + "\n"
    scripts = ""
    if with_flight:
        mid = len(flight) // 2  # split mid-document, as the live page's chunk boundaries do
        scripts = "".join('<script>self.__next_f.push([1,{}])</script>'.format(json.dumps(part)) for part in (flight[:mid], flight[mid:]))
    return ('<!DOCTYPE html><html lang="en"><head><meta charSet="utf-8"/><title>Memberships &amp; Season Passes | Carowinds</title>'
            '<script>(self.__next_f=self.__next_f||[]).push([0])</script></head><body>'
            '<div data-ab="productv2-tabs"><a data-ab="productv2-tab-memberships-and-season-passes">Memberships &amp; Season Passes</a></div>'
            '<div class="tier-cards">{}</div>{}</body></html>').format("".join(card_markup(p) for p in cards), scripts)


def by_title(products, title):
    return next(p for p in products if p["guestFacingTitle"] == title)


def option(product, title):
    return next(o for o in product["paymentOptions"] if o["optionTitle"] == title)


def main(live_path, out_dir):
    live = extract_products(open(live_path, encoding="utf-8").read())
    assert [p["guestFacingTitle"] for p in live] == ["Silver Tier", "Gold Tier", "Prestige Tier"], live
    out = {}

    out["published-page.html"] = page(live)

    reordered = copy.deepcopy([by_title(live, "Prestige Tier"), by_title(live, "Silver Tier"), by_title(live, "Gold Tier")])
    for p in reordered:
        p["paymentOptions"].reverse()  # Season Pass before Membership
        for o in p["paymentOptions"]:
            o["isDefault"] = o["optionTitle"] == "Season Pass"
    out["reordered-page.html"] = page(reordered)

    # The same offers repeated (as in a comparison section) — one observation each.
    out["duplicated-offers-page.html"] = page(live + copy.deepcopy(live))

    conflicting = copy.deepcopy(by_title(live, "Gold Tier"))
    option(conflicting, "Season Pass")["price"] = "$$94"
    out["conflicting-duplicate-page.html"] = page(live + [conflicting], cards=live)

    missing_price = copy.deepcopy(live)
    option(by_title(missing_price, "Gold Tier"), "Membership")["price"] = " "
    out["missing-membership-price-page.html"] = page(missing_price)

    unreadable_renewal = copy.deepcopy(live)
    option(by_title(unreadable_renewal, "Prestige Tier"), "Season Pass")["priceSavings"] = "Renewals Just $$TBD"
    out["unreadable-renewal-page.html"] = page(unreadable_renewal)

    unresolved = copy.deepcopy(live)
    option(by_title(unresolved, "Gold Tier"), "Season Pass")["price"] = "$1f"
    out["unresolved-reference-page.html"] = page(unresolved)

    out["missing-tier-page.html"] = page(copy.deepcopy([by_title(live, "Silver Tier"), by_title(live, "Gold Tier")]))

    unknown = copy.deepcopy(by_title(live, "Gold Tier"))
    unknown["_id"] = "00000000-0000-4000-8000-000000000001"
    unknown["guestFacingTitle"] = "Diamond Tier"
    option(unknown, "Season Pass")["price"] = "$$499"
    option(unknown, "Season Pass")["accessoKeyword"] = "FallDiamondSP2027"
    option(unknown, "Season Pass")["pricePretense"] = None
    option(unknown, "Season Pass")["priceSavings"] = None
    option(unknown, "Membership")["price"] = "$$45"
    option(unknown, "Membership")["accessoKeyword"] = "26DiamondMem2"
    out["unknown-tier-page.html"] = page(copy.deepcopy(live) + [unknown])

    bare = copy.deepcopy(live)
    prestige_pass = option(by_title(bare, "Prestige Tier"), "Season Pass")
    prestige_pass["pricePretense"] = None
    prestige_pass["priceSavings"] = None
    out["no-renewal-no-reference-page.html"] = page(bare)

    extra = copy.deepcopy(live)
    gold_pass = option(by_title(extra, "Gold Tier"), "Season Pass")
    gold_pass["highlightText"] = "Bring-A-Friend for just $$49!"
    gold_membership = option(by_title(extra, "Gold Tier"), "Membership")
    gold_membership["beforePriceText"] = "Limited time"
    extra.append({"_id": "00000000-0000-4000-8000-000000000002", "_type": "product_v2", "guestFacingTitle": "Season Pass Add-On Tier",
                  "currentPrice": "$$35", "currentPriceModifier": "/ea*", "paymentDisplayType": "singlePrice"})
    extra.append({"_id": "00000000-0000-4000-8000-000000000003", "_type": "product_v2", "guestFacingTitle": "Group Outing",
                  "paymentDisplayType": "paymentOptions",
                  "paymentOptions": [{"optionTitle": "Group Ticket", "price": "$$40", "priceFrequency": "/ea*"}]})
    out["unexpected-extra-pricing-page.html"] = page(extra)

    monthly_pass = copy.deepcopy(live)
    option(by_title(monthly_pass, "Silver Tier"), "Season Pass")["priceFrequency"] = "/mo*"
    out["season-pass-shown-monthly-page.html"] = page(monthly_pass)

    truncated_flight = '0:{"P":null}\n5:[{"products":[{"_id":"57a5f9ab-855e-4939-ae44-e6c816f474ec","_type":"product_v2","guestFacingTitle":"Silver Tier","paymentOptions":[{"optionTitle":"Season Pass","price":"$$79"'
    out["truncated-data-page.html"] = page(live, raw_flight=truncated_flight)
    out["cards-without-data-page.html"] = page(live, raw_flight='0:{"P":null}\n1:["$","div",null,{}]\n')
    out["no-page-data.html"] = page(live, with_flight=False)
    out["empty-catalog-page.html"] = page([], raw_flight='0:{"P":null}\n1:["$","div",null,{"children":"Check back soon for 2028 Season Passes."}]\n')

    for name, content in out.items():
        with open(f"{out_dir}/{name}", "w", encoding="utf-8") as handle:
            handle.write(content)
    print("\n".join(sorted(out)))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
