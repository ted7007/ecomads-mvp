var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// src/pages/CampaignPage/components/clusterAssessment.ts
var clusterAssessment_exports = {};
__export(clusterAssessment_exports, {
  clusterAssessment: () => clusterAssessment
});
module.exports = __toCommonJS(clusterAssessment_exports);

// src/shared/lib/formatMoney.ts
function formatMoney(value, fractionDigits = 0) {
  return `${(fractionDigits ? value ?? 0 : Math.round(value ?? 0)).toLocaleString(
    "ru-RU",
    { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }
  )} \u20BD`;
}

// src/shared/lib/formatPercent.ts
function formatPercent(value, fractionDigits = 1) {
  if (value == null || !Number.isFinite(value)) return "\u2014";
  return `${new Intl.NumberFormat("ru-RU", { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }).format(value)}%`;
}

// src/pages/CampaignPage/components/clusterAssessment.ts
function clusterAssessment(evaluation) {
  if (!evaluation) return { title: "\u041D\u0435\u0442 \u043E\u0446\u0435\u043D\u043A\u0438 \u0437\u0430 14 \u0434\u043D\u0435\u0439", reason: "\u041A\u043B\u0430\u0441\u0442\u0435\u0440 \u043E\u0442\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442 \u0432 \u0440\u0430\u0441\u0447\u0451\u0442\u0435 \u0437\u0430 \u044D\u0442\u0438 \u0434\u0430\u0442\u044B.", color: "text.secondary" };
  const rules = evaluation.rules.filter((rule) => rule.enabled && rule.status !== "disabled");
  const triggered = rules.find((rule) => rule.status === "triggered");
  if (triggered) return {
    title: triggered.ruleId === "R03" ? "\u0420\u0430\u0441\u0445\u043E\u0434 \u0431\u0435\u0437 \u0437\u0430\u043A\u0430\u0437\u0430\u043D\u043D\u044B\u0445 \u0435\u0434\u0438\u043D\u0438\u0446" : triggered.ruleId === "R06" ? "\u0414\u0420\u0420 \u0432\u044B\u0448\u0435 \u043D\u043E\u0440\u043C\u044B" : "\u041E\u0431\u043D\u0430\u0440\u0443\u0436\u0435\u043D \u0441\u0438\u0433\u043D\u0430\u043B \u043F\u0440\u043E\u0431\u043B\u0435\u043C\u044B",
    reason: triggered.ruleId === "R03" ? `\u0420\u0430\u0441\u0445\u043E\u0434 ${formatMoney(evaluation.facts.spend)} \xB7 \u0437\u0430\u043A\u0430\u0437\u0430\u043D\u043D\u044B\u0445 \u0435\u0434\u0438\u043D\u0438\u0446 ${evaluation.facts.orderedProducts ?? "\u2014"}.` : triggered.ruleId === "R06" ? `\u0420\u0430\u0441\u0447\u0451\u0442\u043D\u044B\u0439 \u0414\u0420\u0420 ${formatPercent(evaluation.economy.drr === null ? null : evaluation.economy.drr * 100)} \xB7 \u043D\u043E\u0440\u043C\u0430 ${formatPercent(evaluation.norm.value * 100)}. \u041F\u0440\u0435\u0432\u044B\u0448\u0435\u043D\u0438\u0435 \u0441\u043E\u0445\u0440\u0430\u043D\u044F\u0435\u0442\u0441\u044F \u043F\u0440\u0438 \u0438\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u0438 \u0441\u0440\u0435\u0434\u043D\u0435\u0439 \u0446\u0435\u043D\u044B \u0438 \u0434\u043E\u0431\u0430\u0432\u043B\u0435\u043D\u0438\u0438 \u043E\u0434\u043D\u043E\u0439 \u0437\u0430\u043A\u0430\u0437\u0430\u043D\u043D\u043E\u0439 \u0435\u0434\u0438\u043D\u0438\u0446\u044B.` : triggered.explanation,
    color: "warning.main"
  };
  const skipped = rules.find((rule) => rule.status === "skipped");
  if (skipped) return {
    title: skipped.reasonCode === "launch" ? "\u0417\u0430\u0449\u0438\u0442\u0430 \u0437\u0430\u043F\u0443\u0441\u043A\u0430" : "\u041D\u0435\u0434\u043E\u0441\u0442\u0430\u0442\u043E\u0447\u043D\u043E \u0434\u0430\u043D\u043D\u044B\u0445 \u0434\u043B\u044F \u043E\u0446\u0435\u043D\u043A\u0438",
    reason: skipped.explanation,
    color: "text.secondary"
  };
  const lowSample = rules.find((rule) => rule.reasonCode === "low_sample");
  if (lowSample) return { title: "\u041C\u0430\u043B\u043E \u0437\u0430\u043A\u0430\u0437\u0430\u043D\u043D\u044B\u0445 \u0435\u0434\u0438\u043D\u0438\u0446 \u0434\u043B\u044F \u043E\u0446\u0435\u043D\u043A\u0438 \u0414\u0420\u0420", reason: lowSample.explanation, color: "text.secondary" };
  if (!rules.length) return { title: "\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0438 \u043E\u0442\u043A\u043B\u044E\u0447\u0435\u043D\u044B", reason: "\u041E\u0446\u0435\u043D\u043A\u0430 \u044D\u0444\u0444\u0435\u043A\u0442\u0438\u0432\u043D\u043E\u0441\u0442\u0438 \u043D\u0435 \u0432\u044B\u043F\u043E\u043B\u043D\u044F\u043B\u0430\u0441\u044C.", color: "text.secondary" };
  return { title: "\u0421\u0438\u0433\u043D\u0430\u043B\u044B \u043F\u0440\u043E\u0431\u043B\u0435\u043C \u043D\u0435 \u043E\u0431\u043D\u0430\u0440\u0443\u0436\u0435\u043D\u044B", reason: rules.map((rule) => rule.explanation).join(" "), color: "text.secondary" };
}
// Annotate the CommonJS export names for ESM import in node:
0 && (module.exports = {
  clusterAssessment
});
