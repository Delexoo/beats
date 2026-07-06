(function () {
  var viewport = document.getElementById("hero-viewport");
  var stages = viewport ? viewport.querySelectorAll(".hero-stage") : [];
  var dots = document.querySelectorAll(".chapter-dot");
  if (!viewport || !stages.length) return;

  var current = 0;
  var locked = false;
  var lockMs = 650;
  var stageCount = Math.max(stages.length, dots.length);
  var mobileLayout = window.matchMedia("(max-width: 900px)");

  function isMobile() {
    return mobileLayout.matches;
  }

  function updateRail(index) {
    dots.forEach(function (dot, i) {
      var active = i === index;
      dot.classList.toggle("is-active", active);
      if (active) dot.setAttribute("aria-current", "true");
      else dot.removeAttribute("aria-current");
    });
  }

  function setStage(index) {
    current = index;
    stages.forEach(function (stage, i) {
      stage.classList.toggle("is-active", i === index);
    });
    var activeStage = stages[index];
    var stageName = activeStage && activeStage.getAttribute("data-stage-name");
    viewport.setAttribute("data-stage", stageName || String(index));
    updateRail(index);
  }

  function goToStage(index) {
    if (isMobile()) return;
    if (index < 0 || index >= stageCount || index === current || locked) return;
    locked = true;
    setStage(index);
    window.setTimeout(function () { locked = false; }, lockMs);
  }

  dots.forEach(function (dot) {
    dot.addEventListener("click", function () {
      if (isMobile()) return;
      goToStage(parseInt(dot.getAttribute("data-stage"), 10));
    });
  });

  if (!isMobile()) {
    viewport.addEventListener("wheel", function (e) {
      if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return;
      e.preventDefault();
      if (locked) return;
      goToStage(current + (e.deltaY > 0 ? 1 : -1));
    }, { passive: false });
    setStage(0);
  } else {
    stages.forEach(function (stage) {
      stage.classList.add("is-active");
    });
  }
})();
