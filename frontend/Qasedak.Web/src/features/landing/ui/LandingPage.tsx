import Image from "next/image";
import Link from "next/link";
import styles from "./LandingPage.module.css";

const brands = ["فیلیمو", "دیجی‌کالا", "اسنپ", "علی‌بابا", "جاباما", "خانومی"];

const features = [
  { title: "کامنت/لایو هوشمند", symbol: "↗", tone: "gold", bullets: ["تأثیر مثبت روی الگوریتم", "افزایش کامنت و ویو", "تعامل بالا در دایرکت"] },
  { title: "فرم ساز", symbol: "◇", tone: "purple", bullets: ["برگزاری نظرسنجی", "برگزاری آزمون", "دریافت اطلاعات کاربران"] },
  { title: "پاسخ هوشمند اینستاگرام", symbol: "↗", tone: "gold", bullets: ["ارسال ویس تا ۲۰ دقیقه", "ارسال متن و ویدیو", "ارسال لینک و دکمه"] },
  { title: "فالوآپ هوشمند", symbol: "◇", tone: "gold", bullets: ["پیگیری هوشمند ۲۴ ساعته", "تبدیل فالوور به خریدار", "ارسال پیام طبق زمان‌بندی"] },
  { title: "پیامک هوشمند", symbol: "↗", tone: "purple", bullets: ["دریافت شماره تماس کاربران", "ارسال پیامک انبوه", "خروجی اکسل شماره‌ها"] },
  { title: "ویترین ساز", symbol: "◇", tone: "gold", bullets: ["تبدیل دایرکت به فروشگاه", "نمایش محصول و خدمات", "لینک مستقیم و اسکرول افقی"] },
];

const plans = [
  { title: "پکیج ماهانه", tag: null, sub: "یک ماه استفاده آسان و راحت", price: "۲٬۹۲۰٬۰۰۰", tone: "gold" },
  { title: "پکیج سالانه", tag: "پرفروش‌ترین", sub: "تجربه کامل و استثنایی برای شما", price: "۱۵٬۴۱۸٬۰۰۰", tone: "purple", featured: true },
  { title: "پکیج حمایتی", tag: "حمایتی", sub: "برای پیج‌های کمتر از ۲۵ هزار فالوور", price: "۹٬۹۰۰٬۰۰۰", tone: "gold" },
];

const planBenefits = ["دسترسی نامحدود به همه امکانات", "دوره آموزشی رایگان", "مشاوره اختصاصی", "پشتیبانی حرفه‌ای"];

const audiences = [
  { symbol: "↗", tone: "purple", title: "پیج‌های پرمخاطب", description: "مدیریت حرفه‌ای حجم بالای پیام‌ها" },
  { symbol: "◉", tone: "gold", title: "پیج‌های خدماتی", description: "معرفی خدمات و نوبت‌دهی هوشمند" },
  { symbol: "✦", tone: "purple", title: "پیج‌های آموزشی", description: "ارسال سرفصل دوره و لینک ثبت‌نام" },
  { symbol: "▣", tone: "gold", title: "آنلاین‌شاپ‌ها", description: "پاسخ سریع درباره قیمت، موجودی و سفارش" },
];

const customers = [
  { initial: "ع", count: "۳M", name: "علیرضا مطلبی", role: "موسسه پتانسیل", tone: "lilac" },
  { initial: "م", count: "۱M", name: "مهدی ترابی", role: "مدرس فروش", tone: "pink" },
  { initial: "م", count: "۳٫۳M", name: "معین فرجی", role: "مدرس اینستاگرام", tone: "gold" },
  { initial: "م", count: "۴٫۴M", name: "محمدامین کریمپور", role: "بازیگر", tone: "purple" },
];

const faqs = [
  { question: "دایرکت هوشمند اینستاگرام چیست؟", answer: "یک دستیار ۲۴ ساعته برای پاسخ‌گویی خودکار به پیام‌ها بر اساس کلمات کلیدی و سناریوهایی است که شما تعریف می‌کنید." },
  { question: "فعال‌سازی دایرکتم چقدر زمان می‌برد؟", answer: "بعد از ثبت‌نام و اتصال رسمی پیج، در چند دقیقه می‌توانید اولین سناریوی پاسخ خودکار را بسازید." },
  { question: "آیا به رمز اینستاگرام نیاز است؟", answer: "خیر. اتصال از مسیر رسمی اینستاگرام انجام می‌شود و رمز پیج را در اختیار دایرکتم قرار نمی‌دهید." },
  { question: "می‌توانم قبل از خرید رایگان تست کنم؟", answer: "بله؛ مطابق طرح این صفحه، دوره آزمایشی ۱۴ روزه برای شروع و بررسی امکانات در نظر گرفته شده است." },
];

function SectionTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return <div className={styles.sectionTitle}><h2>{title}</h2><i aria-hidden="true" /><p>{subtitle}</p></div>;
}

function Brand() {
  return <span className={styles.brand}><b>DM</b><strong>دایرکتم</strong></span>;
}

export function LandingPage() {
  return (
    <main className={styles.page}>
      <a className={styles.skipLink} href="#main-content">رفتن به محتوای اصلی</a>

      <div className={styles.promoBar}>
        <div className={styles.container}>
          <span>واحد فروش دایرکتم: ۰۲۱ ۹۱ ۶۹ ۰۶ ۶۵</span>
          <strong>سرویس دایرکتم، ۱۴ روز رایگان شد</strong>
          <Link href="/register">دریافت فوری اشتراک</Link>
        </div>
      </div>

      <header className={styles.header}>
        <div className={styles.container}>
          <Link href="/" aria-label="صفحه اصلی دایرکتم"><Brand /></Link>
          <nav className={styles.desktopNav} aria-label="منوی اصلی">
            <a href="#how-it-works" className={styles.activeNav}>دایرکت هوشمند</a>
            <a href="#activation">آموزش پنل دایرکتم</a>
            <a href="#features">کامنت هوشمند اینستاگرام</a>
            <a href="#features">آپشن‌های دایرکتم</a>
            <a href="#about">مقالات</a>
            <a href="#faq">سوالات متداول</a>
            <a href="#about">اخبار دایرکتم</a>
            <a href="#pricing">تعرفه</a>
            <a href="#about">درباره ما</a>
          </nav>
          <div className={styles.headerActions}>
            <Link href="/register" className={styles.buyButton}>خرید اشتراک</Link>
            <Link href="/login" className={styles.loginButton}>ورود</Link>
            <span className={styles.searchGlyph} aria-hidden="true">⌕</span>
          </div>
          <details className={styles.mobileMenu}>
            <summary aria-label="باز کردن منوی اصلی"><span /><span /><span /></summary>
            <nav aria-label="منوی موبایل"><a href="#how-it-works">دایرکت هوشمند</a><a href="#features">امکانات</a><a href="#activation">فعال‌سازی</a><a href="#pricing">تعرفه</a><a href="#faq">سوالات متداول</a><Link href="/login">ورود</Link><Link href="/register" className={styles.mobileBuy}>۱۴ روز رایگان شروع کنید</Link></nav>
          </details>
        </div>
      </header>

      <section className={styles.hero} id="main-content">
        <div className={styles.heroImage}>
          <Image src="/landing/directam-team.webp" alt="تیم دایرکتم" fill priority sizes="(max-width: 760px) 78vw, 356px" />
        </div>
        <div className={styles.heroCopy}>
          <h1>دایرکتم، دایرکت هوشمند اینستاگرام</h1>
          <p>دایرکت هوشمند یک دستیار ۲۴ ساعته برای صفحه اینستاگرام شماست؛ هیچ پیامی بدون پاسخ نمی‌ماند. سیستم بر اساس تنظیماتی که شما انجام داده‌اید، پاسخ مناسب را فوری ارسال می‌کند.</p>
          <p>با دایرکتم یک تجربه لذت‌بخش از پاسخ اتوماتیک دایرکت و فروش آنلاین داشته باشید.</p>
          <div><Link href="/register" className={styles.heroFree}>رایگان شروع کنید</Link><Link href="/register" className={styles.heroConsult}>مشاوره رایگان و خرید</Link></div>
        </div>
      </section>

      <section className={styles.trustSection} aria-labelledby="trust-title">
        <SectionTitle title="برندهایی که به دایرکت هوشمند اینستاگرام دایرکتم اعتماد کردند" subtitle="" />
        <div className={`${styles.container} ${styles.brandGrid}`}>{brands.map((brand, index) => <div className={index % 2 ? styles.brandPurple : ""} key={brand}>{brand}</div>)}</div>
      </section>

      <section className={styles.howSection} id="how-it-works">
        <SectionTitle title="دایرکت هوشمند چطور کار می‌کند؟" subtitle="اکنون هزاران نفر از اتوماسیون دایرکت برای پاسخ‌گویی سریع‌تر استفاده می‌کنند" />
        <div className={`${styles.container} ${styles.howGrid}`}>
          <div className={styles.videoCard}><Image src="/landing/directam-video.webp" alt="ویدیوی معرفی کوتاه دایرکتم" fill sizes="(max-width: 760px) 92vw, 390px" /><span aria-hidden="true">▶</span></div>
          <p>سرویس دایرکت هوشمند تمام نیازهای پیج‌های بیزینسی، آنلاین‌شاپ، آموزشی و خدماتی را در پاسخ‌دهی اتوماتیک پوشش می‌دهد. این سیستم با تشخیص کلمات کلیدی مانند «قیمت» یا «سفارش»، متن، عکس، فایل و لینک مناسب را برای مخاطب می‌فرستد.<br /><br />اتصال به اینستاگرام و واتساپ، مسیر ارتباط و خرید مشتری را یکپارچه می‌کند؛ حتی وقتی آنلاین نیستید.</p>
        </div>
      </section>

      <section className={styles.advantageSection}>
        <div className={`${styles.container} ${styles.advantageGrid}`}>
          <div><h2>بهترین دایرکت هوشمند</h2><p>دایرکتم با تمرکز بر نیازهای فارسی‌زبانان، ابزارهایی مثل ویترین‌ساز، پیامک هوشمند، دکمه‌های شیشه‌ای و ارسال ویس طولانی را در اختیار شما می‌گذارد. پشتیبانی حرفه‌ای تیم ما کمک می‌کند سناریوهای فروش را سریع و دقیق اجرا کنید.</p></div>
          <div className={styles.supportImage}><Image src="/landing/directam-support.webp" alt="راهنمایی و پشتیبانی دایرکتم" fill sizes="(max-width: 760px) 86vw, 350px" /></div>
        </div>
        <div className={styles.salesCopy}><h2>آیا دایرکت هوشمند باعث افزایش فروش می‌شود؟</h2><p>بله؛ پاسخ‌گویی سریع اعتماد مخاطب را بیشتر می‌کند و سیستم می‌تواند کاربر را مستقیماً به لینک پرداخت، فرم دریافت اطلاعات یا واتساپ فروش هدایت کند. حتی اگر مشتری نیمه‌شب قیمت بپرسد، همان لحظه پاسخ و لینک خرید را دریافت می‌کند.</p></div>
      </section>

      <section className={styles.featuresSection} id="features">
        <SectionTitle title="قابلیت بی‌نظیر دایرکت اتوماتیک اینستاگرام" subtitle="همه‌چیز برای تعامل بیشتر، پاسخ سریع‌تر و فروش حرفه‌ای‌تر" />
        <div className={`${styles.container} ${styles.featureGrid}`}>{features.map((feature) => <article key={feature.title}><div className={`${styles.featureSymbol} ${feature.tone === "purple" ? styles.purple : styles.gold}`}>{feature.symbol}</div><h3>{feature.title}</h3><ul>{feature.bullets.map((bullet) => <li key={bullet}>{bullet}</li>)}</ul><a href="#activation">توضیحات بیشتر</a></article>)}</div>
      </section>

      <section className={styles.consultationSection}><div className={`${styles.container} ${styles.consultationCard}`}><div><h2>مشاوره نیاز داری؟</h2><p>پشتیبانامون منتظر پیام شما هستن</p></div><div><a href="tel:+982191690665" className={styles.phoneButton}>۰۲۱-۹۱۶۹-۰۶۶۵</a><Link href="/register" className={styles.freeConsultButton}>مشاوره رایگان</Link></div></div></section>

      <section className={styles.activationSection} id="activation">
        <SectionTitle title="خرید و نحوه فعالسازی دایرکت هوشمند اینستاگرام" subtitle="زیر ۵ دقیقه فعالش کن" />
        <p className={styles.activationCopy}>برای استفاده از دایرکتم نیازی به دانش فنی پیچیده ندارید. در سایت ثبت‌نام کنید، پیج اینستاگرام خود را به پنل متصل کنید و سناریوهای پاسخ خودکار را بسازید.</p>
        <div className={`${styles.container} ${styles.stepsCard}`}>{["ثبت‌نام در پنل", "انتخاب و خرید اشتراک", "اتصال حساب اینستاگرام", "ساخت اولین سناریو"].map((step, index) => <div key={step}><span>{index + 1}</span><b>{step}</b></div>)}</div>
      </section>

      <section className={styles.pricingSection} id="pricing">
        <SectionTitle title="تعرفه‌های دایرکتم" subtitle="پکیج مناسب پیج خود را انتخاب کنید و یک گام به رشد هوشمند نزدیک‌تر شوید" />
        <div className={`${styles.container} ${styles.planGrid}`}>{plans.map((plan) => <article className={plan.featured ? styles.planFeatured : ""} key={plan.title}><header className={plan.tone === "purple" ? styles.purple : styles.gold}><h3>{plan.title}</h3></header>{plan.tag ? <span className={styles.planTag}>{plan.tag}</span> : null}<p>{plan.sub}</p><strong>{plan.price}</strong><small>تومان</small><ul>{planBenefits.map((benefit) => <li key={benefit}>✓&nbsp; {benefit}</li>)}</ul><Link href="/register" className={plan.featured ? styles.planPrimary : styles.planOutline}>انتخاب</Link></article>)}</div>
      </section>

      <section className={styles.audienceSection}>
        <SectionTitle title="دایرکت هوشمند برای چه پیج‌هایی مناسب است؟" subtitle="برای هر صفحه‌ای که روزانه با مشتریانش گفتگو می‌کند" />
        <div className={`${styles.container} ${styles.audienceGrid}`}>{audiences.map((item) => <article key={item.title}><span className={item.tone === "purple" ? styles.purple : styles.gold}>{item.symbol}</span><h3>{item.title}</h3><p>{item.description}</p></article>)}</div>
      </section>

      <section className={styles.customersSection}>
        <SectionTitle title="همراهان دایرکت خودکار اینستاگرام" subtitle="برندها، مدرسان و کسب‌وکارهایی که با دایرکتم رشد کرده‌اند" />
        <div className={`${styles.container} ${styles.customerGrid}`}>{customers.map((customer) => <article key={customer.name}><span className={styles[customer.tone]}>{customer.initial}</span><strong>{customer.count}</strong><h3>{customer.name}</h3><p>{customer.role}</p></article>)}</div>
      </section>

      <section className={styles.faqSection} id="faq">
        <SectionTitle title="سوالات متداول" subtitle="پاسخ کوتاه به سوال‌های پرتکرار شما" />
        <div className={`${styles.container} ${styles.faqList}`}>{faqs.map((faq) => <details key={faq.question}><summary><span>{faq.question}</span><b aria-hidden="true">+</b></summary><p>{faq.answer}</p></details>)}</div>
      </section>

      <footer className={styles.footer} id="about">
        <div className={`${styles.container} ${styles.footerGrid}`}>
          <div className={styles.footerAbout}><Brand /><p>دستیار هوشمند ۲۴ ساعته برای پاسخ‌گویی، تعامل و فروش بیشتر در اینستاگرام.</p></div>
          <div><h3>دسترسی سریع</h3><a href="#pricing">تعرفه‌ها</a><a href="#activation">آموزش‌ها</a><a href="#how-it-works">مقالات</a><a href="#about">درباره ما</a></div>
          <div><h3>ارتباط با ما</h3><a href="tel:+982191690665">۰۲۱-۹۱۶۹-۰۶۶۵</a><Link href="/dashboard/help">پشتیبانی آنلاین</Link><span>اینستاگرام دایرکتم</span></div>
          <Link href="/register" className={styles.footerCta}>۱۴ روز رایگان شروع کنید</Link>
        </div>
        <div className={`${styles.container} ${styles.copyright}`}>© تمامی حقوق برای دایرکتم محفوظ است</div>
      </footer>
    </main>
  );
}
