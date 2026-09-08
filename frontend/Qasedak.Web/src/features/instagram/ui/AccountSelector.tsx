"use client";

import type { ConnectionState } from "../health";
import { accountLabel, eligibleAccounts } from "../selection";
import styles from "./InstagramSurface.module.css";

export function AccountSelector({
  accounts,
  selectedAccountId,
  onChange,
  disabled = false,
}: {
  accounts: readonly ConnectionState[];
  selectedAccountId: string | null;
  onChange: (accountId: string | null) => void;
  disabled?: boolean;
}) {
  const available = eligibleAccounts(accounts);
  return (
    <div className={styles.selectorCard}>
      <label className={styles.selectorLabel} htmlFor="instagram-account-selector">حساب اینستاگرام</label>
      <select
        id="instagram-account-selector"
        className={styles.selector}
        value={selectedAccountId ?? ""}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value || null)}
      >
        <option value="">یک حساب را انتخاب کنید</option>
        {available.map((account) => (
          <option key={account.accountId} value={account.accountId}>{accountLabel(account)}</option>
        ))}
      </select>
      <span className={styles.selectorHint}>
        انتخاب حساب صریح است؛ قاصدک هیچ‌وقت اولین حساب ورک‌اسپیس را خودکار انتخاب نمی‌کند.
      </span>
    </div>
  );
}
