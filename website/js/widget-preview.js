(function () {
  var screen = document.getElementById("desktop-screen");
  var wrap = document.getElementById("desktop-widget-wrap");
  var handle = document.getElementById("pill-drag-handle");
  var hideTab = document.getElementById("desktop-hide-tab");
  if (!screen || !wrap || !handle || window.matchMedia("(max-width: 900px)").matches) return;

  var dragging = false;
  var isHidden = false;
  var animating = false;
  var offsetX = 0;
  var offsetY = 0;
  var visibleX = null;
  var visibleY = null;
  var activePointerId = null;
  var screenRect = null;
  var moveRaf = 0;
  var pendingLeft = 0;
  var pendingTop = 0;
  var slideMs = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 380;

  function readScreenRect() {
    screenRect = screen.getBoundingClientRect();
  }

  function clampPosition(left, top) {
    var maxLeft = Math.max(0, screen.clientWidth - wrap.offsetWidth);
    var maxTop = Math.max(0, screen.clientHeight - wrap.offsetHeight);
    return {
      left: Math.max(0, Math.min(left, maxLeft)),
      top: Math.max(0, Math.min(top, maxTop))
    };
  }

  function applyPosition(left, top) {
    var pos = clampPosition(left, top);
    visibleX = pos.left;
    visibleY = pos.top;
    wrap.style.left = pos.left + "px";
    wrap.style.top = pos.top + "px";
    wrap.style.transform = "none";
    wrap.classList.add("is-positioned");
    return pos;
  }

  function ensurePixelPosition() {
    if (visibleX !== null && visibleY !== null) return;
    readScreenRect();
    var wr = wrap.getBoundingClientRect();
    applyPosition(wr.left - screenRect.left, wr.top - screenRect.top);
  }

  function initPosition() {
    readScreenRect();
    var w = wrap.offsetWidth;
    var h = wrap.offsetHeight;
    applyPosition(screen.clientWidth * 0.5 - w * 0.5, screen.clientHeight * 0.42 - h * 0.5);
  }

  function slideOffTarget(left, top) {
    var w = wrap.offsetWidth;
    var h = wrap.offsetHeight;
    var sw = screen.clientWidth;
    var sh = screen.clientHeight;
    var cx = left + w / 2;
    var cy = top + h / 2;
    var dLeft = cx;
    var dRight = sw - cx;
    var dTop = cy;
    var dBottom = sh - cy;
    var min = Math.min(dLeft, dRight, dTop, dBottom);
    if (min === dLeft) return { left: -w - 20, top: top };
    if (min === dRight) return { left: sw + 20, top: top };
    if (min === dTop) return { left: left, top: -h - 20 };
    return { left: left, top: sh + 20 };
  }

  function updateHideTab() {
    if (!hideTab) return;
    hideTab.classList.toggle("is-widget-hidden", isHidden);
    hideTab.setAttribute("aria-label", isHidden ? "Show widget" : "Hide widget");
  }

  function finishAnimation() {
    animating = false;
    wrap.classList.remove("is-animating");
  }

  function toggleVisibility() {
    if (animating) return;
    ensurePixelPosition();

    if (!isHidden) {
      var off = slideOffTarget(visibleX, visibleY);
      animating = true;
      wrap.classList.add("is-animating");
      wrap.style.left = off.left + "px";
      wrap.style.top = off.top + "px";
      wrap.classList.add("is-hidden");
      isHidden = true;
      updateHideTab();
      if (slideMs === 0) finishAnimation();
      else window.setTimeout(finishAnimation, slideMs);
      return;
    }

    animating = true;
    wrap.classList.add("is-animating");
    wrap.style.left = visibleX + "px";
    wrap.style.top = visibleY + "px";
    wrap.classList.remove("is-hidden");
    isHidden = false;
    updateHideTab();
    if (slideMs === 0) finishAnimation();
    else window.setTimeout(finishAnimation, slideMs);
  }

  function isBackslashKey(e) {
    return e.code === "Backslash" || e.code === "IntlBackslash";
  }

  function isHideHotkey(e) {
    if (e.shiftKey || e.ctrlKey || e.metaKey) return false;
    if (!e.altKey || !isBackslashKey(e)) return false;
    return true;
  }

  document.addEventListener("keydown", function (e) {
    if (!isHideHotkey(e)) return;
    e.preventDefault();
    toggleVisibility();
  }, true);

  if (hideTab) hideTab.addEventListener("click", toggleVisibility);

  function flushMove() {
    moveRaf = 0;
    applyPosition(pendingLeft, pendingTop);
  }

  function scheduleMove(left, top) {
    pendingLeft = left;
    pendingTop = top;
    if (moveRaf) return;
    moveRaf = window.requestAnimationFrame(flushMove);
  }

  function onPointerMove(e) {
    if (!dragging || e.pointerId !== activePointerId) return;
    if (!screenRect) readScreenRect();
    scheduleMove(
      e.clientX - screenRect.left - offsetX,
      e.clientY - screenRect.top - offsetY
    );
  }

  function endDrag(e) {
    if (!dragging) return;
    if (e && e.pointerId !== activePointerId) return;
    dragging = false;
    activePointerId = null;
    if (moveRaf) {
      window.cancelAnimationFrame(moveRaf);
      moveRaf = 0;
      flushMove();
    }
    wrap.classList.remove("is-dragging");
    handle.classList.remove("is-dragging");
    document.removeEventListener("pointermove", onPointerMove);
    document.removeEventListener("pointerup", endDrag);
    document.removeEventListener("pointercancel", endDrag);
  }

  handle.addEventListener("pointerdown", function (e) {
    if (isHidden || animating || e.button !== 0) return;
    wrap.classList.add("is-dragging");
    wrap.classList.remove("is-animating");
    ensurePixelPosition();
    readScreenRect();
    dragging = true;
    activePointerId = e.pointerId;
    handle.classList.add("is-dragging");
    var wr = wrap.getBoundingClientRect();
    offsetX = e.clientX - wr.left;
    offsetY = e.clientY - wr.top;
    if (handle.setPointerCapture) {
      try { handle.setPointerCapture(e.pointerId); } catch (err) { /* ignore */ }
    }
    document.addEventListener("pointermove", onPointerMove);
    document.addEventListener("pointerup", endDrag);
    document.addEventListener("pointercancel", endDrag);
    e.preventDefault();
  });

  window.addEventListener("resize", function () {
    readScreenRect();
    if (visibleX === null || visibleY === null) return;
    if (isHidden) return;
    applyPosition(visibleX, visibleY);
  });

  window.requestAnimationFrame(function () {
    window.requestAnimationFrame(initPosition);
  });
})();
