/* Echoes the endpoint an administrator picked, so the choice is visible before
 * they act on it.
 *
 * Markup contract: [data-deployment-endpoint] is the select,
 * [data-deployment-endpoint-help] the element that describes the choice.
 */
(() => {
    'use strict';

    const select = document.querySelector('[data-deployment-endpoint]');
    const help = document.querySelector('[data-deployment-endpoint-help]');
    if (!select || !help) return;

    const describe = () => { help.textContent = `Selected endpoint: ${select.value}`; };
    select.addEventListener('change', describe);
})();
