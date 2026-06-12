const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
        if (!entry.isIntersecting) continue;

        const image = entry.target;
        observer.unobserve(image);
        const src = image.dataset.lazySrc || "";
        if (!src) return;

        image.addEventListener("load", () => {
            image.classList.add("is-loaded");
            image.parentElement?.classList.add("is-loaded");
        }, { once: true });
        image.src = src;
    }
}, { root: null, rootMargin: "600px 0px", threshold: 0.01 });

export function observeLazyImage(root, image, src) {
    if (!root || !image || !src) return;

    image.classList.remove("is-loaded");
    root.classList.remove("is-loaded");
    image.removeAttribute("src");
    image.dataset.lazySrc = src;
    observer.observe(image);
}

export function unobserveLazyImage(image) {
    if (!image) return;

    observer.unobserve(image);
    delete image.dataset.lazySrc;
}
