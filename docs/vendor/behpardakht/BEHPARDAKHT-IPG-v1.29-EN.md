# Behpardakht Mellat Internet Payment Gateway (IPG)
## English Technical Translation / Implementation Reference

> **Source document:** Behpardakht Mellat Internet Payment Gateway User Guide - Functions and Methods  
> **Source version:** 1.29  
> **Source date:** Tir 1402 (June/July 2023)  
> **Original language:** Persian  
> **Source label inside the PDF:** "Unofficial - External"  
> **Purpose of this file:** English implementation reference for Qasedak engineering agents.
>
> This file is a faithful technical translation and normalization of the supplied Persian v1.29 guide. It is **not an official first-party English document** and does not claim that the endpoints or operational policies have remained unchanged after v1.29. For production onboarding, merchant credentials, IP/domain registration, and any newer Behpardakht instructions supplied to the merchant remain authoritative.

---

## Qasedak Integration Scope

For the normal Qasedak subscription/payment flow, the relevant Behpardakht operations are:

1. `bpPayRequest`
2. Redirect customer to Behpardakht payment page
3. Receive HTTP POST callback
4. Validate callback against the original server-side payment attempt
5. `bpVerifyRequest`
6. `bpSettleRequest`
7. `bpInquiryRequest` when the verify result is unknown
8. `bpReversalRequest` when the payment status cannot be established and service must not be delivered

The following operations exist in the source guide but are **not required for Qasedak v1 unless explicitly added later**:

- `bpChargePayRequest`
- `bpRefundRequest`
- `bpRefundToPANRequest`
- `bpDynamicPayRequest`
- `bpCumulativeDynamicPayRequest`

Qasedak MUST preserve its current provider-neutral `IPaymentGateway` architecture. Behpardakht-specific SOAP DTOs, response parsing, and transport rules belong in Infrastructure.

---

# 1. Revision Information

## 1.1 Document Revision History

| Version | Approx. date | Change |
|---|---|---|
| 0.90 | Mehr 1388 | Initial unfinished draft |
| 0.91 | Dey 1388 | Added production server addresses and `bpGetSuccessfulSales` |
| 0.92 | Bahman 1388 | Changed production addresses to SSL |
| 0.93 | Farvardin 1389 | Added `PayerId` to `bpPayRequest` |
| 0.94 | Farvardin 1389 | Changed test server addresses |
| 0.95 | Khordad 1389 | Added request-number explanations |
| 0.96 | Mordad 1389 | Updated some status-code information |
| 0.97 | Mehr 1389 | Added type-two payment: `bpDynamicPayRequest` |
| 0.98 | Dey 1392 | Changed Web Service address |
| 0.99 | Ordibehesht 1393 | Added mobile-recharge payment service: `bpChargePayRequest` |
| 1.00 | Shahrivar 1393 | Increased expected Verify window from 15 to 20 minutes |
| 1.01 | Shahrivar 1393 | Added English payment page |
| 1.02 | Dey 1393 | Added Refund method |
| 1.03 | Bahman 1393 | Added security notes |
| 1.04 | Aban 1394 | Changes for Iranian-goods credit-purchase transactions |
| 1.05 | Shahrivar 1395 | Added response-code actions and refund-acceptance conditions |
| 1.06 | Mehr 1395 | Added display of extra merchant descriptions |
| 1.07 | Mordad 1396 | Added refund capability for POS transactions |
| 1.08 | Mehr 1396 | Added refund to a specific PAN |
| 1.09 | Azar 1396 | Added merchant-supplied cardholder mobile number for stored-card retrieval |
| 1.10 | Dey 1396 | Added ability for merchant to send a card number to the payment gateway |
| 1.11 | Shahrivar 1397 | Added refund to the card of a purchase transaction via `bpRefundToPANRequest` |
| 1.13 | Bahman 1397 | Added ability to store more than one card for a customer when a mobile identifier is supplied |
| 1.14 | Esfand 1397 | Required callback path to belong to the merchant's registered site domain |
| 1.15 | Khordad 1398 | Added `bpCumulativeDynamicRequest` / cumulative dynamic payment |
| 1.16 | Bahman 1398 | Added error code 98 |
| 1.17 | Ordibehesht 1399 | Added customer national-ID matching against bank-card ownership |
| 1.19 | Khordad 1400 | Added optional parameters to `bpPayRequest` |
| 1.20 | Khordad 1400 | Parameter spelling/casing corrections (`MobileNo`, `HiddenMode`, `CardHolderPan`) |
| 1.21 | Shahrivar 1400 | Refund-related notes and parameter changes |
| 1.22 | Aban 1400 | Added/changed mobile-number fields for refund operations |
| 1.23 | Tir 1401 | Added 12-digit cardholder profile identifier for retrieving stored cards |
| 1.24 | Mordad 1401 | Removed `PosRefundRequest`; updated refund naming/fields |
| 1.25 | Mehr 1401 | Added merchant name/address display on redirect page |
| 1.26 | Dey 1401 | Added national-ID/cardholder checks and mandatory redirect `Referer` domain validation |
| 1.27 | Bahman 1401 | Updated refund fields and `PayerId` type. **Current v1.29 request table defines `payerId` as `string`.** |
| 1.28 | Khordad 1402 | Expanded national-ID/cardholder verification information |
| 1.29 | Tir 1402 | Updated mobile-recharge payment service (`bpChargePayRequest`) |

---

# 2. Introduction

Behpardakht Mellat Internet Payment Gateway uses **Web Services** to connect the merchant site to the bank/payment gateway.

At the lower protocol layer, communication uses **SOAP**, which represents and structures data using **XML**. At the transport layer, communication uses **HTTP or HTTPS**.

The merchant may implement its site using any technology capable of calling the provided Web Service methods.

## 2.1 Scope

The document describes the Behpardakht Mellat Internet Payment Gateway framework, the functions/methods used by merchants, their inputs and outputs, and the normal integration flow.

---

# 3. Terminology and Method Meanings

The source states that the first three methods form the main normal payment flow. Later methods are used for exceptional or specialized cases.

### `bpPayRequest`
Payment-request method. The merchant requests a card payment and submits transaction amount, merchant-side order number, merchant credentials, callback URL, and related information.

### `bpChargePayRequest`
Similar to `bpPayRequest`, but intended for mobile-recharge payment products.

### `bpVerifyRequest`
Payment verification. The merchant calls this method to confirm that the payment was successfully charged and recorded by the bank/gateway.

### `bpSettleRequest`
Settlement request. After the customer has paid and the transaction has been verified, the merchant calls this operation to finalize settlement of the transaction to the merchant account according to the merchant agreement.

### `bpInquiryRequest`
Transaction inquiry. Used when the merchant cannot determine the result of a previous verify operation and needs to query the transaction state.

### `bpReversalRequest`
Reversal request. Used when the merchant cannot establish the transaction state and will not provide the product/service. It requests reversal if money was charged.

### `bpDynamicPayRequest`
Type-two payment request. Similar to `bpPayRequest` but includes an account/sub-service identifier for dynamic destination-account behavior configured with the bank.

### `bpCumulativeDynamicPayRequest`
Cumulative type-two payment. Similar to `bpPayRequest` but supports a set of destination/sub-service IDs, amounts, and payer identifiers in one payment.

### `bpRefundRequest`
Refund for a settled Internet/POS transaction. Supports full or partial refund, subject to the gateway's rules.

### `bpRefundToPANRequest`
Refund to a specified card or to the card associated with a specified purchase transaction.

---

# 4. Common Parameter Definitions

| Parameter | Meaning |
|---|---|
| `terminalId` | Merchant Internet terminal number |
| `userName` | Merchant username |
| `userPassword` | Merchant password |
| `amount` | Transaction amount |
| `localDate` | Request date, `YYYYMMDD` |
| `localTime` | Request time, `HHMMSS` |
| `additionalData` | Merchant-defined additional transaction data |
| `callBackUrl` | Merchant callback URL |
| `payerId` | Payer identifier |
| `orderId` | Merchant request/order identifier. For payment requests this must be unique. |
| `saleOrderId` | The purchase order identifier. In later stages it identifies the successful Sale transaction and corresponds to the payment-stage `orderId`. |
| `saleReferenceId` | Bank/gateway purchase reference identifier returned after a successful payment |
| `subServiceId` | Preconfigured account/sub-service identifier used for type-two payment |

---

# 5. Prerequisites

The source guide specifies the following prerequisites:

1. **Merchant server IP must be registered/whitelisted with Behpardakht.**
   - If the server IP has not been supplied to and registered by Behpardakht, the service will reject requests.
2. The merchant must receive:
   - terminal number,
   - username,
   - password.
3. The merchant host must be able to reach the required service ports.
   - The guide explicitly mentions ports **443** and **80**.
4. The callback URL must belong to the merchant domain registered with Behpardakht.
5. Redirect-domain validation is enforced; see the security notes below.

---

# 6. Web Service and Production Addresses

## 6.1 Production SOAP Web Service

```text
https://bpm.shaparak.ir/pgwchannel/services/pgw?wsdl
```

The guide identifies the following principal methods on the Web Service:

```text
bpPayRequest
bpChargePayRequest
bpVerifyRequest
bpSettleRequest
bpInquiryRequest
bpReversalRequest
bpRefundRequest
bpDynamicPayRequest
bpCumulativeDynamicPayRequest
```

For a normal electronic payment, the guide identifies these as the main operations:

```text
bpPayRequest
bpVerifyRequest
bpSettleRequest
```

`bpInquiryRequest` and `bpReversalRequest` are used for exceptional/unknown-result conditions.

---

# 7. Normal Payment Flow

The normal source-defined flow is:

```text
Merchant server
    |
    | bpPayRequest
    v
Behpardakht SOAP service
    |
    | returns ResCode,RefId
    v
Merchant redirects/posts customer with RefId
    |
    v
Behpardakht payment page
    |
    | customer pays
    v
POST callback to merchant callBackUrl
    |
    | validate callback against original PaymentAttempt
    v
bpVerifyRequest
    |
    | success
    v
bpSettleRequest
    |
    v
Final successful payment
```

When verification status is unknown:

```text
bpVerifyRequest result unknown
    |
    v
bpInquiryRequest
    |
    +-- status established --> continue according to status
    |
    +-- status still unknown --> bpReversalRequest
```

---

# 8. `bpPayRequest` / `bpChargePayRequest`

## 8.1 Purpose

The merchant requests a payment transaction. If the merchant credentials/request are accepted, the gateway returns a unique reference.

The returned value is a string with two parts, for example:

```text
0,AF82041a2Bf6989c7fF9
```

- First part: `ResCode`
- Second part: `RefId`

When `ResCode == 0`, the returned `RefId` must be posted to the payment page.

## 8.2 Payment-page URLs

### Persian payment page

```text
https://bpm.shaparak.ir/pgwchannel/startpay.mellat
```

### English payment page

```text
https://bpm.shaparak.ir/pgwchannel/enstartpay.mellat
```

### Iranian-goods credit purchase page

```text
https://bpm.shaparak.ir/pgwCreditchannel/startpay.mellat
```

## 8.3 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `10` | Merchant payment request ID; must be unique for payment requests |
| 5 | `amount` | `long` | `1` | Purchase amount |
| 6 | `localDate` | `string` | `20091008` | Request date, `YYYYMMDD` |
| 7 | `localTime` | `string` | `102003` | Request time, `HHMMSS` |
| 8 | `additionalData` | `string` | up to 1000 chars | Merchant-defined transaction data |
| 9 | `callBackUrl` | `string` | `http://www.mysite.com/myfolder/callbackmellat.aspx` | Callback URL; must be under the merchant's registered domain |
| 10 | `payerId` | `string` | `0` | Payer identifier |
| 11 | `mobileNo` | `string` | `989125305269` | Optional cardholder mobile/profile identifier |
| 12 | `encPan` | `string` | `701EE799BCB9B5D4` | Optional encrypted card number |
| 13 | `panHiddenMode` | `string` | `1` | Optional card-number display mode |
| 14 | `cartItem` | `string` | - | Optional merchant text shown on gateway |
| 15 | `enc` | `string` | `04EAE799BC894BFF` | Optional encrypted national ID |

## 8.4 Redirect Form

For the normal Qasedak flow, posting the returned `RefId` is the essential operation:

```html
<form
  action="https://bpm.shaparak.ir/pgwchannel/startpay.mellat"
  method="post">
  <input type="hidden" name="RefId" value="THE_REF_ID" />
  <button type="submit">Pay</button>
</form>
```

The original guide also documents optional redirect fields such as:

- `MobileNo`
- `HiddenMode`
- `EncPan`
- `ENC`
- `merchantName`
- `merchantAddress`

These optional legacy/card-prefill/national-ID facilities are outside Qasedak's normal v1 subscription checkout and SHOULD NOT be implemented unless there is an explicit product/security requirement.

## 8.5 Important Rules

- Every payment request must use a unique `orderId`.
- Prefer a domain URL rather than a raw IP in `callBackUrl`.
- `callBackUrl` must belong to the domain registered for the merchant.
- `RefId` is **case-sensitive**.
- Parameter names/casing and value formats must match the contract exactly.
- The source requires the HTTP `Referer` header on the redirect request to contain the merchant site's domain. The gateway compares it with the registered domain/subdomain and may reject mismatches.
- Optional payer/account-specific behavior depends on merchant configuration with Behpardakht.

---

# 9. Callback From the Payment Gateway

After the cardholder completes the gateway interaction, Behpardakht POSTs the result to the merchant's `callBackUrl`.

## 9.1 Callback Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `RefId` | `string` | `AF82041a2Bf6989c7fF9` | Payment-request reference generated by `bpPayRequest` |
| 2 | `ResCode` | `string` | `0` | Purchase status / response code |
| 3 | `SaleOrderId` | `long` | `10` | Purchase order ID |
| 4 | `SaleReferenceId` | `long` | `127926981246` | Bank/gateway reference ID for the purchase transaction |
| 5 | `CardHolderPan` | `string` | `610433*****5689` | First 6 and last 4 digits of card |
| 6 | `CreditCardSaleResponseDetail` | `string` | `0` | Iranian-goods credit-purchase result details |
| 7 | `FinalAmount` | `long` | `480000` | Final amount charged in online-discount scenarios |

## 9.2 Mandatory Security Validation

The source guide explicitly requires the merchant, after receiving the callback, to verify that:

- callback `RefId` exactly equals the `RefId` originally returned by the corresponding Pay request;
- callback `SaleOrderId` exactly equals the order ID associated with the same original transaction.

If `RefId` or `SaleOrderId` does not match the original server-side transaction, the merchant must treat the callback as invalid and **must not call `bpVerifyRequest` for it**.

For Qasedak this means:

```text
Never trust callback query/form fields as transaction authority.
Load PaymentAttempt from server-side storage.
Compare callback identity to the original attempt.
Only then perform server-to-server verification.
```

---

# 10. `bpVerifyRequest`

## 10.1 Purpose

Used after the customer completes payment to confirm the successful purchase with the gateway.

The returned value is a response-code string.

## 10.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `11` | Verify request ID |
| 5 | `saleOrderId` | `long` | `10` | Purchase order ID; same purchase identifier from the Sale stage |
| 6 | `saleReferenceId` | `long` | `127926981246` | Bank purchase reference |

## 10.3 Rules

- A callback `ResCode` of `0` indicates that the card payment stage succeeded; the merchant must still call `bpVerifyRequest` to verify it server-to-server.
- The guide allows the verify `orderId` to be the same as `saleOrderId`; uniqueness is not stated as mandatory for verify requests.
- The guide states that if a successful Sale is not verified within approximately **20 minutes**, the gateway sends an automatic reversal request and the transaction is considered failed, with funds returned to the cardholder.
- The source also discusses repeat verification in non-final states (including already-verified / already-reversed states). Qasedak MUST implement bounded, idempotent handling rather than an unbounded retry loop.

---

# 11. `bpSettleRequest`

## 11.1 Purpose

Finalizes settlement of a verified transaction.

The guide states that return value `0` means the merchant settlement request was accepted successfully.

A successful transaction that is not settled is not considered finalized for merchant settlement.

## 11.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `12` | Settlement request ID |
| 5 | `saleOrderId` | `long` | `10` | Purchase order ID |
| 6 | `saleReferenceId` | `long` | `127926981246` | Same bank reference used during Verify |

The source permits settlement `orderId` to equal `saleOrderId`.

---

# 12. `bpInquiryRequest`

## 12.1 Purpose

Used when the merchant did not receive or cannot determine the result of `bpVerifyRequest`.

It requests the current transaction status.

## 12.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `13` | Inquiry request ID |
| 5 | `saleOrderId` | `long` | `10` | Purchase order ID |
| 6 | `saleReferenceId` | `long` | `127926981246` | Purchase reference ID |

The source permits inquiry `orderId` to equal `saleOrderId`.

---

# 13. `bpReversalRequest`

## 13.1 Purpose

Used when payment status remains unclear and the merchant will not provide the product/service.

The merchant asks the gateway/bank to reverse the transaction if funds were deducted.

The source says this method is used after the verification path and states a maximum reversal-notification window of approximately **3 hours after Verify**.

It also notes that return of deducted funds can occur by the end of the current day, provided settlement has not already been requested.

## 13.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `14` | Reversal request ID |
| 5 | `saleOrderId` | `long` | `10` | Purchase order ID |
| 6 | `saleReferenceId` | `long` | `127926981246` | Purchase reference ID |

The source permits reversal `orderId` to equal `saleOrderId`.

---

# 14. `bpRefundRequest`

## 14.1 Purpose

Refund all or part of an Internet/POS purchase amount to the customer card, provided the purchase transaction has already been settled and `bpSettleRequest` was called.

## 14.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `userName` | `string` | `******` | Merchant username |
| 3 | `userPassword` | `string` | `******` | Merchant password |
| 4 | `orderId` | `long` | `16` | Refund request ID; must be unique for each refund call |
| 5 | `saleOrderId` | `long` | `10` | Original purchase order ID |
| 6 | `saleReferenceId` | `long` | `127926981246` | Original purchase reference |
| 7 | `refundAmount` | `long` | `500` | Amount to refund |

## 14.3 Rules

- Multiple refund calls may be made for one purchase as long as total refunded amount does not exceed the purchase amount.
- Each refund `orderId` must be unique.
- If a refund call times out or returns a non-zero code, the guide instructs the merchant to first use inquiry/status methods to confirm the refund failed before retrying it.

---

# 15. `bpRefundToPANRequest`

## 15.1 Purpose

Refund a specified amount either:

- to a specified PAN, or
- to the card associated with a specified purchase reference.

## 15.2 Input Parameters

| # | Parameter | Type | Example | Description |
|---:|---|---|---|---|
| 1 | `terminalId` | `long` | `1234` | Merchant terminal number |
| 2 | `User` | `string` | `******` | Merchant username |
| 3 | `Password` | `string` | `******` | Merchant password |
| 4 | `PAN` | `long` | `6104337116619294` | Destination card number |
| 5 | `SaleReferenceId` | `long` | `127926981246` | Purchase reference whose PAN is to be retrieved |
| 6 | `Amount` | `long` | `50000` | Refund amount |
| 7 | `orderId` | `long` | `16` | Unique refund request ID |
| 8 | `mobileNumber` | `string` | `989122222222` | Optional destination-cardholder mobile number |

`PAN` and `SaleReferenceId` are optional alternatives; one of them must be present.

The source states the response contains a `ResponseCode` and `ReferenceNumber`.

---

# 16. `bpDynamicPayRequest`

## 16.1 Purpose

Type-two payment, similar to `bpPayRequest`, with a preconfigured `subServiceId`.

Successful response format is the same general form:

```text
0,AF82041a2Bf6989c7fF9
```

The returned `RefId` is posted to:

```text
https://bpm.shaparak.ir/pgwchannel/startpay.mellat
```

## 16.2 Input Parameters

| # | Parameter | Type | Description |
|---:|---|---|---|
| 1 | `terminalId` | `long` | Merchant terminal number |
| 2 | `userName` | `string` | Merchant username |
| 3 | `userPassword` | `string` | Merchant password |
| 4 | `orderId` | `long` | Unique payment request ID |
| 5 | `amount` | `long` | Purchase amount |
| 6 | `localDate` | `string` | `YYYYMMDD` |
| 7 | `localTime` | `string` | `HHMMSS` |
| 8 | `additionalData` | `string` | Up to 1000 characters |
| 9 | `callBackUrl` | `string` | Registered-domain callback URL |
| 10 | `payerId` | `string` | Payer identifier |
| 11 | `subServiceId` | `long` | Destination/sub-service identifier |
| 12 | `mobileNo` | `string` | Optional mobile/profile identifier |
| 13 | `encPan` | `string` | Optional encrypted PAN |
| 14 | `panHiddenMode` | `string` | Optional PAN display mode |
| 15 | `cartItem` | `string` | Optional gateway display text |
| 16 | `enc` | `string` | Optional encrypted national ID |

---

# 17. `bpCumulativeDynamicPayRequest`

## 17.1 Purpose

Cumulative type-two payment. Allows multiple destination/sub-service identifiers and amounts in a single payment.

The successful result is the same general form:

```text
0,AF82041a2Bf6989c7fF9
```

The returned `RefId` is posted to:

```text
https://bpm.shaparak.ir/pgwchannel/startpay.mellat
```

## 17.2 Input Parameters

| # | Parameter | Type | Description |
|---:|---|---|---|
| 1 | `terminalId` | `long` | Merchant terminal number |
| 2 | `userName` | `string` | Merchant username |
| 3 | `userPassword` | `string` | Merchant password |
| 4 | `orderId` | `long` | Unique payment request ID |
| 5 | `amount` | `long` | Total purchase amount |
| 6 | `localDate` | `string` | `YYYYMMDD` |
| 7 | `localTime` | `string` | `HHMMSS` |
| 8 | `additionalData` | `string` | Up to 10 destination/sub-service entries; fields separated with commas and entries separated with semicolons |
| 9 | `callBackUrl` | `string` | Merchant callback URL |
| 10 | `mobileNo` | `string` | Optional mobile/profile identifier |
| 11 | `encPan` | `string` | Optional encrypted PAN |
| 12 | `panHiddenMode` | `string` | Optional PAN display mode |
| 13 | `cartItem` | `string` | Optional gateway display text |
| 14 | `enc` | `string` | Optional encrypted national ID |

Example `additionalData` concept from the guide:

```text
88,140000,108;74,12000,;
```

---

# 18. Optional Redirect/Card/National-ID Features

The source guide contains examples for:

- sending `MobileNo` with `RefId` to allow gateway-side retrieval of previously used cards;
- sending an encrypted PAN using DES;
- setting a hidden/display mode for the PAN;
- sending an encrypted national ID (`ENC`) for cardholder identity matching;
- merchant name and address display on the gateway page.

These are **optional and outside Qasedak's normal v1 subscription payment scope**.

The original v1.29 document includes legacy cryptographic examples and fixed-key examples. Qasedak MUST NOT copy those examples into production merely because they appear in this guide. If any such optional feature is later required, obtain the merchant-specific/current Behpardakht/Shaparak security contract and perform a separate security review.

---

# 19. Response Codes (`ResCode`)

The source guide defines the following response codes.

| Code | Meaning | Source notes / action |
|---:|---|---|
| `0` | Transaction completed successfully | Success |
| `11` | Invalid card number | |
| `12` | Insufficient balance | For refund operations, may also indicate insufficient merchant refund credit; contact Behpardakht to increase credit |
| `13` | Incorrect PIN/password | |
| `14` | PIN/password attempts exceeded | |
| `15` | Invalid card | |
| `16` | Withdrawal/transaction attempts exceeded | |
| `17` | User cancelled the transaction | |
| `18` | Card expired | |
| `19` | Amount exceeds permitted limit | For refunds, may mean total requested refund exceeds purchase amount |
| `111` | Invalid card issuer | |
| `112` | Issuer switch error | |
| `113` | No response from card issuer | |
| `114` | Cardholder not permitted to perform this transaction | |
| `21` | Invalid merchant | Source notes that the service may not be enabled for the merchant; contact Behpardakht |
| `23` | Security error | |
| `24` | Invalid merchant credentials | |
| `25` | Invalid amount | |
| `31` | Invalid response | |
| `32` | Invalid input-data format | |
| `33` | Invalid account | |
| `34` | System error | |
| `35` | Invalid date | |
| `41` | Duplicate request number | |
| `42` | Sale transaction not found | For refunds, the corresponding successful purchase was not found or was unsuccessful |
| `43` | Verify already requested / transaction already verified | The guide says a previously successful verify may be treated as successful |
| `44` | Verify request not found | |
| `45` | Transaction already settled | The guide says a previously successful settlement may be treated as successful |
| `46` | Transaction not settled | |
| `47` | Settlement transaction not found | |
| `48` | Transaction reversed | Previously reversed by merchant or automatically after verify timeout; funds returned to cardholder |
| `412` | Invalid bill identifier | |
| `413` | Invalid payment identifier | |
| `414` | Invalid bill-issuing organization | |
| `415` | Working session expired | |
| `416` | Error registering information | |
| `417` | Invalid payer identifier | |
| `418` | Error defining customer information | |
| `419` | Information-entry attempts exceeded | |
| `421` | Invalid IP | Merchant server IP was not previously registered; contact Behpardakht |
| `51` | Duplicate transaction | |
| `54` | Reference transaction does not exist | |
| `55` | Invalid transaction | |
| `61` | Settlement/deposit error | |
| `62` | Callback path is not under merchant's registered domain | Contact Behpardakht if registered domain must be changed |
| `98` | Static-password usage limit reached | |
| `995` | Card ownership could not be verified against customer identity | |

---

# 20. Additional Source Rules

## 20.1 Session Timeout

If the merchant application uses a user session during the payment flow, the guide recommends a site session timeout of at least **15 minutes** so the session does not expire before the payment cycle completes.

It also notes that firewalls may independently manage session timeout.

## 20.2 `RefId`

`RefId` is case-sensitive and must be returned exactly as received.

## 20.3 Callback Domain

The callback route must belong to the merchant's registered domain.

## 20.4 Redirect `Referer`

The guide requires the redirect request to contain a `Referer` header whose domain matches the registered merchant domain/subdomain.

## 20.5 Merchant IP

Behpardakht must have the merchant server IP registered before Web Service access is permitted.

---

# 21. Qasedak Engineering Contract

The following section is **Qasedak-specific engineering guidance derived from the source contract and the project's existing billing architecture**. It is not a literal section of the Persian document.

## 21.1 Provider Identifier

```text
mellat
```

## 21.2 Required Server-Side Configuration

At minimum:

```text
terminalId
userName
userPassword
soapServiceUrl
paymentPageUrl
callbackBaseUrl
enabled
```

Use secret/typed server-side configuration. Never expose credentials to Next.js/browser code and never commit real credentials.

Recommended production defaults from the supplied v1.29 source:

```text
SOAP WSDL:
https://bpm.shaparak.ir/pgwchannel/services/pgw?wsdl

Persian payment page:
https://bpm.shaparak.ir/pgwchannel/startpay.mellat
```

Configuration SHOULD remain overridable so merchant-specific/newer onboarding instructions can replace these without source-code changes.

## 21.3 Internal Currency

Qasedak's existing canonical billing representation is IRR.

Do not perform implicit IRR/IRT conversion in the frontend.

The server owns the amount used to create `bpPayRequest`.

## 21.4 PaymentAttempt Mapping

Recommended provider-specific fields mapped into Qasedak's durable PaymentAttempt state:

```text
providerId         = "mellat"
orderId            = merchant-side unique payment order
providerToken      = RefId
saleOrderId        = SaleOrderId
providerReference  = SaleReferenceId
callbackResCode    = callback ResCode
verifiedAt
settledAt
```

Keep provider-specific fields in Infrastructure/persistence metadata according to the existing Qasedak architecture; do not leak SOAP models into Domain.

## 21.5 Exactly-Once Rules

Qasedak must continue enforcing:

- unique server-generated payment attempt;
- server-owned amount;
- callback `RefId` must match stored `RefId`;
- callback `SaleOrderId` must match stored order;
- callback alone never grants entitlement;
- `bpVerifyRequest` must succeed or resolve to an explicitly successful idempotent state;
- `bpSettleRequest` must succeed or resolve to an explicitly already-settled successful state;
- subscription/entitlement activation occurs exactly once;
- duplicate callbacks are harmless;
- concurrent callback processing has one durable winner;
- timeouts/unknown results are reconciled using Inquiry before unsafe retry/reversal decisions;
- payment failures never activate entitlement.

## 21.6 Suggested State Machine

```text
Created
  |
  v
PayRequested
  |
  | ResCode == 0 + RefId persisted
  v
Redirected
  |
  v
CallbackReceived
  |
  | identity check: RefId + SaleOrderId
  v
Verifying
  |
  +-- verify success / already verified --> Settling
  |
  +-- unknown --> InquiryPending
  |
  +-- definitive failure --> Failed

InquiryPending
  |
  +-- successful verified state --> Settling
  |
  +-- definitive failure --> Failed
  |
  +-- unresolved per contract --> ReversalPending

Settling
  |
  +-- settle success / already settled --> Paid
  |
  +-- definitive failure --> SettlementFailed

Paid
  |
  v
Grant subscription/entitlement exactly once
```

Do not infer undocumented success semantics beyond the source response codes and current merchant contract.

## 21.7 HTTP Callback Handling

The callback endpoint must accept gateway POST form fields.

Safe flow:

```text
POST callback
-> parse bounded known fields
-> locate PaymentAttempt
-> constant/strict identity comparisons
-> acquire concurrency/idempotency protection
-> verify server-to-server
-> settle server-to-server
-> atomically finalize PaymentAttempt
-> atomically grant entitlement once
-> redirect/render a safe result page
```

Never trust:

- browser-submitted amount,
- plan ID from callback,
- callback success label alone,
- callback `ResCode` alone as final payment authority.

## 21.8 SOAP Client Boundary

A recommended Infrastructure boundary is:

```text
BehpardakhtMellatPaymentGateway
    |
    v
IBehpardakhtSoapClient
    |
    +-- bpPayRequest
    +-- bpVerifyRequest
    +-- bpSettleRequest
    +-- bpInquiryRequest
    +-- bpReversalRequest
```

This allows deterministic tests without calling the real gateway.

Whether the implementation uses generated SOAP client code or a small explicit SOAP envelope client is an Infrastructure choice. Keep it behind the internal interface and test exact serialization/parsing.

## 21.9 CI/Test Requirements

CI must not make live Behpardakht calls.

Required deterministic tests should cover:

### Pay
- success response `0,RefId`
- non-zero `ResCode`
- malformed response
- missing `RefId`
- timeout
- SOAP fault

### Callback
- matching `RefId` and `SaleOrderId`
- mismatched `RefId`
- mismatched `SaleOrderId`
- missing fields
- cancelled/non-zero callback
- duplicate callback

### Verify
- `0` success
- `43` already verified treated idempotently according to source
- reversed state (`48`)
- unknown/timeout leading to Inquiry
- malformed/fault response

### Settle
- `0` success
- `45` already settled treated idempotently according to source
- settlement failure
- timeout/fault

### Inquiry/Reversal
- unknown verify result reconciliation
- definitive failed state
- reversal path when required
- no entitlement if status remains unresolved

### Persistence/concurrency
- one entitlement grant under duplicate callbacks
- one entitlement grant under concurrent verification
- durable `RefId`/`SaleReferenceId`
- provider-token uniqueness where appropriate
- amount remains server-owned

---

# 22. Production Readiness Checklist

Before enabling Behpardakht in production:

- [ ] Current merchant Terminal ID received
- [ ] Current merchant username received
- [ ] Current merchant password received
- [ ] Production server public IP registered/whitelisted with Behpardakht
- [ ] Callback domain/path registered with Behpardakht
- [ ] Production callback URL uses HTTPS
- [ ] Outbound access to Behpardakht service permitted
- [ ] Current merchant onboarding instructions compared with this v1.29 source
- [ ] SOAP service URL confirmed
- [ ] Payment-page URL confirmed
- [ ] `Referer` behavior confirmed in deployed reverse-proxy/browser flow
- [ ] Test transaction succeeds with real merchant credentials
- [ ] Callback identity checks verified
- [ ] Verify succeeds
- [ ] Settle succeeds
- [ ] Duplicate callback smoke test is idempotent
- [ ] Production secrets are not in Git
- [ ] Logs do not contain merchant password or sensitive raw payloads
- [ ] Monitoring/correlation IDs enabled
- [ ] Rollback/disable switch for provider tested

---

# 23. Source-vs-Implementation Caution

This translation is based on the supplied **Behpardakht Mellat IPG User Guide v1.29, Tir 1402**.

The PDF itself is marked **"Unofficial - External"**. Therefore:

1. It is suitable as the explicit technical source supplied by the project owner for implementing the v1.29 contract.
2. It should be preserved as a versioned vendor reference.
3. Production credentials and merchant-specific onboarding instructions must still be checked.
4. If a newer Behpardakht document conflicts with this file, create a new ADR/vendor-reference revision rather than silently changing transport behavior.

---

# 24. Recommended Repository Placement

Recommended committed path for this English reference:

```text
docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md
```

Recommended location for implementation-specific evidence/ADR references:

```text
docs/vendor/behpardakht/
docs/architecture/   # or the repository's existing ADR location
```

If the original Persian PDF is kept in the repository, use a separate source directory:

```text
docs/vendor/behpardakht/source/behpardakht-ipg-fa-v1.29.pdf
```

Because the supplied PDF came from a third-party mirror and is marked "Unofficial - External", consider keeping the original PDF outside the public Git repository if redistribution/licensing is uncertain. The English project reference can remain committed for engineering use.

---

# 25. Minimal Qasedak Flow Summary

```text
Create PaymentAttempt
    |
    | server-owned amount / IRR
    v
bpPayRequest
    |
    | "0,RefId"
    v
Persist exact RefId
    |
    v
POST RefId -> startpay.mellat
    |
    v
Gateway callback POST
    |
    | validate RefId + SaleOrderId
    v
bpVerifyRequest
    |
    | verified
    v
bpSettleRequest
    |
    | settled
    v
Atomic Paid transition
    |
    v
Grant subscription/entitlement EXACTLY ONCE
```

**Never grant entitlement from the callback alone.**
