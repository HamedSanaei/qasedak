# Qasedak final Penpot design specification (extracted live via MCP)

Source: canonical file `c269caa0-e456-818c-8008-85a77340be64`, extracted 2026-08-24 via
Penpot MCP (`penpotUtils.getPageById` → `openPage` → board tree inspection). This file is
the implementation contract for the visual reconciliation; values below are authoritative
and were read from the live design, not invented.

## Shared tokens

| Token | Value |
| --- | --- |
| Page/canvas bg | `#f6f7f9` |
| Brand primary | `#be0183` |
| Brand deep (auth brand panel) | `#670048`, decor ellipses `#8e0062` / `#a90075` |
| Brand soft | `#fcebf6` |
| Ink / headings | `#2e2938` |
| Body secondary | `#7d7887`; muted `#a09ba8`; nav text `#514d5e` |
| Card border | `#e3e5e8`; sidebar border `#e8e9ec`; divider `#e3e5e8`; disabled fill `#ecedef` |
| Success | `#168b5b` on `#e9f7f1` |
| Danger | `#c93c54` on `#fff0f3` |
| Warning | `#a8640a` on `#fff6e6` |
| Info | `#2f6fed` on `#edf3ff` |
| Font | Vazirmatn everywhere; RTL layout |

Radii: auth cards/brand panel 28 · panels/cards 14–18 · inputs/buttons 10 · chips 9–12.
Buttons h≈52 primary filled `#be0183` white text fs14; disabled `#d8d5db`.
Inputs h54 white r10 border `#e3e5e8`, placeholder `#a09ba8`.

## Identity & Workspace (page `c48311ed-e700-80f8-8008-881f0352eb6a`)

Boards: Login/Desktop `…881f0372388a`, Register/Desktop `…881f075bc2f7`,
Login/Mobile `…881f0b6a5a33`, Register/Mobile `…881f0cbbe326`,
States `…881f0ea618ba`.

Layout (desktop): split hero — left brand panel (560×896 @64,64) `#670048` r28 with two
decor ellipses; white ق mark r12 + wordmark «قاصدک» fs24; promise headline fs32 white
«ارتباط‌های ارزشمند، ساده و یکپارچه»; body fs16 `#f7d8ec`; three benefits with green ✓
circles (#168b5b) fs14. Right auth card (644×816 @708,104) white r28 stroke #e3e5e8:
brand mark row, eyebrow fs14 brand («ورود به حساب»/«ساخت حساب»), title fs28, subtitle
fs14 #7d7887, labeled inputs (ایمیل placeholder `name@example.com` LTR-left;
گذرواژه placeholder «حداقل ۱۰ کاراکتر»), login page adds helper note fs12 «در صورت
فراموشی گذرواژه با مدیر فضای کاری تماس بگیرید.» + security note box #edf3ff r12
(title fs13 #2f6fed «نشانی صفحه را بررسی کنید», body fs12). Register page instead has
نام نمایشی field (placeholder «مثلاً حامد محمودی») + policy box #f6f7f9 r12
(«گذرواژه باید:» fs13 + two bullet dots fs12: بین ۱۰ تا ۱۲۸ کاراکتر باشد / فقط از حروف
و اعداد تشکیل نشده باشد). Primary button labels: «ورود» / «ساخت حساب». Cross-links:
«حساب ندارید؟ ثبت‌نام» and «قبلاً ثبت‌نام کرده‌اید؟ ورود» (link text brand color).
Mobile boards stack the card full-width without the brand panel.

States board defines: form errors (email invalid, wrong password without account
disclosure, email taken), processing (disabled fields+button, label «در حال ورود…»,
double-submit blocked), server error (form preserved + retry «تلاش دوباره» danger),
401 session expired (warning action «ورود دوباره», return to destination after login),
workspace creation after register (name required, owner role), 403 (no data shown,
return to workspace selection), workspace-ready success (name/role + enter dashboard),
account-without-workspace info state.

## Inbox & Conversations (page `c48311ed-e700-80f8-8008-88200ec40bf3`)

Boards: Desktop `…88200ed6b9fc`, Mobile inbox/thread `…88201670e15e`/`…88201a3b1157`,
Tablet `…88201bd8a56d`, Product States `…8820201f874b`.

Three-panel desktop over canvas #f6f7f9 with existing sidebar component instance +
white topbar r12 (breadcrumb fs13 «داشبورد / صندوق گفتگو», workspace fs13).

- List panel white r14: title «گفتگوها» fs20; search input DISABLED by design
  (bg #f6f7f9 r10, placeholder «جستجو — پس از تکمیل query backend», warning badge
  «فعلاً غیرفعال» #fff6e6/#a8640a); filter chips باز/خوانده‌نشده/بایگانی (active chip
  brand-soft bg with brand text); conversation rows r12 (selected = #fcebf6): avatar
  circle initial, participant fs14 ink, preview fs12 #7d7887, time fs11 muted, unread
  badge brand circle white count. Footer pagination bar #f6f7f9 r10 «۱–۴ از ۲۴ گفتگو».
- Thread panel white r14: header avatar+participant fs15 + status fs11 success «گفتگوی
  باز • پاسخ تا ۲۲ ساعت دیگر»; day separator «امروز»; incoming bubbles white r14 stroke,
  outgoing bubbles #fcebf6 (time fs10 brand); delivery note pill #e9f7f1 «پیام‌های
  پذیرفته‌شده توسط کانال ثبت می‌شوند.»; composer area #f6f7f9 r14 containing white
  composer r12 (placeholder «پیام خود را بنویسید…», counter «۰ / ۱۰۰۰», send button
  brand «ارسال») + note «ارسال فقط در بازه مجاز ۲۴ ساعته».
- Context panel white r14 «اطلاعات گفتگو»: avatar IG, id fs14, channel line fs12
  success «اینستاگرام • متصل»; زمینه اخیر block; future-CRM placeholder box (#f6f7f9,
  نام مخاطب/برچسب‌ها/یادداشت‌ها muted + badge «غیرفعال») and warning «Tags و Notes تا
  تکمیل M07 قابل ویرایش نیستند.»

Product States board covers loading skeletons, empty list, error retry, disconnected
account state, mobile thread back-navigation.

## Billing & Payments (page `c48311ed-e700-80f8-8008-8820a6cf5187`)

Boards: Plans `…8820a7020aa1`, Current Subscription `…8820adebc780`, Checkout
`…8820b1f8bfe9`, Payment Results `…8820b826931b`, Checkout/Mobile `…8820bd6206dd`.

- Plans: title «انتخاب اشتراک» fs28; intro banner #fcebf6 r16 (title fs18 brand, body
  fs13: prices come from Billing API; no hardcoded numbers are source of truth); period
  selector pill (ماهانه/سالانه, selected chip brand-soft w/ brand text — display-only);
  three plan cards white r16 (badge «پیشنهادی» brand pill on recommended; title fs20;
  price line fs15 «قیمت و دوره از سرور»; tax note fs12 «مبلغ نهایی پیش از انتقال نمایش
  داده می‌شود.»; divider; entitlement feature rows with ✓ circles; footer button
  «انتخاب پلن» neutral / «انتخاب‌شده» brand when selected); one-off payment notice
  #fff6e6 (تمدید با پرداخت درگاه / no auto-charge in v1); price-contract card
  (server-owned amounts).
- Current Subscription: active badge #e9f7f1 «فعال» + brand stripe; plan name fs24;
  meta «دوره، تاریخ شروع و پایان از سرور»; entitlement summary rows «سطح دسترسی فعلی»;
  CTA «تمدید اشتراک»; renewal card (پلن/دوره انتخاب → پرداخت جدید) with «شروع تمدید»;
  payment history panel «تاریخچه پرداخت» (muted title, empty-state badge).
- Checkout: order summary card (پلن انتخابی، مبلغ/ارز/تخفیف همه «مقدار سرور»، total box
  «فقط مقدار تأییدشده سرور» brand, note «این مبلغ از query مرورگر یا callback خوانده
  نمی‌شود.», secure note #edf3ff); provider selection card: radio options زرین‌پال
  (brand icon «زر», meta «انتقال به صفحه پرداخت زرین‌پال») and پرداخت مستقیم بانک ملی
  (soft icon «ملی», meta notes merchant contract must be confirmed); one-off charge
  warning; CTA «ادامه به درگاه»; terms line; redirect state note (button disabled while
  redirecting, double-submit blocked).
- Payment Results: five result cards — pending (#edf3ff … «در حال بررسی پرداخت» +
  progress track), success (#e9f7f1 OK «پرداخت موفق» + «مشاهده اشتراک»), failed
  (#fff0f3 × «پرداخت ناموفق» + «تلاش دوباره»), cancelled (#fff6e6 × «پرداخت لغو شد» +
  retry), already-verified (#e9f7f1 OK «قبلاً تأیید شده» — subscription not extended
  again), verification-error (؟ «خطا در بررسی» + «بررسی وضعیت»). Idempotency note:
  repeated callbacks surface the same attempt; entitlement applies once.
