const state = {
  categories: [
    { file: "overview.json", label: "标准化概况" },
    { file: "blocks.json", label: "方块" },
    { file: "items.json", label: "物品" },
    { file: "creatures-and-entities.json", label: "生物与实体" },
    { file: "rocks-minerals-and-metals.json", label: "岩石、矿物与金属" },
    { file: "food-farming-and-cooking.json", label: "食物、农业与烹饪" },
    { file: "crafting-and-processing.json", label: "工艺与加工" },
    { file: "mechanical-power.json", label: "机械动力" },
    { file: "environment-and-worldgen.json", label: "环境与世界生成" },
    { file: "biomes.json", label: "生物群系" },
    { file: "status-stats-and-damage.json", label: "状态、属性与伤害" },
    { file: "ui-commands-and-server.json", label: "界面、命令与服务器" },
    { file: "temporal-lore-and-story.json", label: "时空、传说与剧情" },
    { file: "mod-common-terms.json", label: "模组通用术语" },
    { file: "technical-content.json", label: "技术性内容" },
    { file: "other.json", label: "其他" },
  ],
  loadedCategories: [],
  query: "",
};

const els = {
  summary: document.querySelector("#summary"),
  search: document.querySelector("#searchInput"),
  nav: document.querySelector("#categoryNav"),
  content: document.querySelector("#content"),
  empty: document.querySelector("#emptyState"),
};

const collator = new Intl.Collator(["zh-CN", "en"], {
  numeric: true,
  sensitivity: "base",
});

async function loadTerminology() {
  const categories = await Promise.all(
    state.categories.map(async (category) => {
      const response = await fetch(`../../projects/translation-terminology/zh-cn/${category.file}`);
      if (!response.ok) {
        throw new Error(`无法读取 ${category.file}`);
      }

      const terms = await response.json();
      const entries = Object.entries(terms)
        .map(([term, translation]) => ({
          term,
          translation,
          searchText: `${term}\n${translation}\n${category.label}`.toLowerCase(),
        }))
        .sort((a, b) => collator.compare(a.term, b.term));

      return {
        ...category,
        id: category.file.replace(/\.json$/, ""),
        entries,
      };
    }),
  );

  state.loadedCategories = categories;
  render();
}

function render() {
  const query = state.query.trim().toLowerCase();
  const filtered = state.loadedCategories
    .map((category) => ({
      ...category,
      entries: query
        ? category.entries.filter((entry) => entry.searchText.includes(query))
        : category.entries,
    }))
    .filter((category) => category.entries.length > 0);

  const total = state.loadedCategories.reduce((sum, category) => sum + category.entries.length, 0);
  const shown = filtered.reduce((sum, category) => sum + category.entries.length, 0);

  els.summary.textContent = query
    ? `共 ${total} 条术语，当前显示 ${shown} 条匹配结果`
    : `共 ${total} 条术语，按 ${state.loadedCategories.length} 个分类展示`;

  renderNav(filtered);
  renderCategories(filtered);
  els.empty.hidden = filtered.length > 0;
}

function renderNav(categories) {
  els.nav.replaceChildren(
    ...categories.map((category) => {
      const link = document.createElement("a");
      link.className = "category-pill";
      link.href = `#${category.id}`;
      link.textContent = `${category.label} ${category.entries.length}`;
      return link;
    }),
  );
}

function renderCategories(categories) {
  els.content.replaceChildren(
    ...categories.map((category) => {
      const section = document.createElement("section");
      section.className = "category";
      section.id = category.id;

      const header = document.createElement("header");
      header.className = "category-header";

      const title = document.createElement("h2");
      title.textContent = category.label;

      const count = document.createElement("span");
      count.textContent = `${category.entries.length} 条`;

      header.append(title, count);
      section.append(header, createTable(category.entries));
      return section;
    }),
  );
}

function createTable(entries) {
  const wrap = document.createElement("div");
  wrap.className = "table-wrap";

  const table = document.createElement("table");
  const thead = document.createElement("thead");
  const tbody = document.createElement("tbody");

  const headRow = document.createElement("tr");
  ["英文术语", "标准译名"].forEach((text) => {
    const th = document.createElement("th");
    th.textContent = text;
    headRow.append(th);
  });
  thead.append(headRow);

  entries.forEach((entry) => {
    const row = document.createElement("tr");
    const term = document.createElement("td");
    const translation = document.createElement("td");

    term.className = "term";
    translation.className = "translation";
    term.textContent = entry.term;
    translation.textContent = entry.translation;

    row.append(term, translation);
    tbody.append(row);
  });

  table.append(thead, tbody);
  wrap.append(table);
  return wrap;
}

els.search.addEventListener("input", (event) => {
  state.query = event.target.value;
  render();
});

loadTerminology().catch((error) => {
  els.summary.textContent = error.message;
  els.empty.hidden = false;
  els.empty.textContent = "术语表载入失败，请检查数据文件是否存在。";
});
