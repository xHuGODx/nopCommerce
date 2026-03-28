import http from 'k6/http';
import { check, fail, group, sleep } from 'k6';
import exec from 'k6/execution';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost').replace(/\/+$/, '');
const PRODUCT_PATH = __ENV.PRODUCT_PATH || '/htc-smartphone';
const PAYMENT_METHOD = 'Payments.Manual';

const VUS = Number(__ENV.VUS || 1);
const ITERATIONS = Number(__ENV.ITERATIONS || 1);
const PAUSE_SECONDS = Number(__ENV.PAUSE_SECONDS || 0);

export const options = {
  scenarios: {
    checkout_failure_missing_payment_info: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: ITERATIONS,
      maxDuration: __ENV.MAX_DURATION || '15m',
    },
  },
};

const FORM_HEADERS = {
  'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
};

export default function () {
  const identity = `${exec.vu.idInTest}-${exec.scenario.iterationInTest}`;
  const email = `k6-failed-checkout-${identity}@example.test`;

  group('guest-checkout-failure-missing-payment-info', () => {
    const productResponse = groupRequest('load product page', 'GET', `${BASE_URL}${PRODUCT_PATH}`);
    const productToken = extractRequestVerificationToken(productResponse.body, 'product page');
    const addToCartPath = extractFirstMatch(
      productResponse.body,
      /\/addproducttocart\/details\/\d+\/1/i,
      'add-to-cart path'
    );
    const quantityField = extractFirstMatch(
      productResponse.body,
      /addtocart_\d+\.EnteredQuantity/i,
      'quantity field'
    );

    const addToCartResponse = groupRequest('add product to cart', 'POST', `${BASE_URL}${addToCartPath}`, {
      __RequestVerificationToken: productToken,
      [quantityField]: '1',
    });
    const addToCartJson = parseJson(addToCartResponse.body, 'add-to-cart response');
    if (!addToCartJson.success) {
      fail(`Add-to-cart failed: ${addToCartResponse.body}`);
    }

    const cartResponse = groupRequest('load cart', 'GET', `${BASE_URL}/cart`);
    const cartToken = extractRequestVerificationToken(cartResponse.body, 'cart page');
    const checkoutAttributes = extractCheckoutAttributes(cartResponse.body);
    if (Object.keys(checkoutAttributes).length > 0) {
      groupRequest(
        'save checkout attributes',
        'POST',
        `${BASE_URL}/shoppingcart/checkoutattributechange/%7BisEditable%7D?isEditable=True`,
        {
          __RequestVerificationToken: cartToken,
          ...checkoutAttributes,
        }
      );
    }

    const checkoutPageResponse = groupRequest('load one-page checkout', 'GET', `${BASE_URL}/checkout/onepagecheckout/`);
    const checkoutToken = extractRequestVerificationToken(checkoutPageResponse.body, 'checkout page');
    const countryId = getSelectedOrFirstOptionValue(checkoutPageResponse.body, 'BillingNewAddress.CountryId');
    const stateProvinceId = getSelectedOrFirstOptionValue(checkoutPageResponse.body, 'BillingNewAddress.StateProvinceId');

    const billingResponse = groupRequest('save billing address', 'POST', `${BASE_URL}/checkout/OpcSaveBilling/`, {
      __RequestVerificationToken: checkoutToken,
      billing_address_id: '0',
      ShipToSameAddress: 'true',
      'BillingNewAddress.Id': '0',
      'BillingNewAddress.FirstName': 'Load',
      'BillingNewAddress.LastName': 'Tester',
      'BillingNewAddress.Email': email,
      'BillingNewAddress.Company': '',
      'BillingNewAddress.CountryId': countryId,
      'BillingNewAddress.StateProvinceId': stateProvinceId,
      'BillingNewAddress.County': '',
      'BillingNewAddress.City': 'Austin',
      'BillingNewAddress.Address1': '123 Test Street',
      'BillingNewAddress.Address2': '',
      'BillingNewAddress.ZipPostalCode': '78701',
      'BillingNewAddress.PhoneNumber': '5551234567',
      'BillingNewAddress.FaxNumber': '',
    });
    const billingJson = parseJson(billingResponse.body, 'billing response');
    if (billingJson.error) {
      fail(`Billing step failed: ${billingResponse.body}`);
    }

    const shippingMethodHtml = billingJson.update_section?.html || '';
    const shippingOption = extractCheckedOrFirstRadioValue(shippingMethodHtml, 'shippingoption');
    const shippingResponse = groupRequest(
      'save shipping method',
      'POST',
      `${BASE_URL}/checkout/OpcSaveShippingMethod/`,
      {
        __RequestVerificationToken: checkoutToken,
        shippingoption: shippingOption,
      }
    );
    const shippingJson = parseJson(shippingResponse.body, 'shipping response');
    if (shippingJson.error) {
      fail(`Shipping step failed: ${shippingResponse.body}`);
    }

    const paymentResponse = groupRequest(
      'save payment method',
      'POST',
      `${BASE_URL}/checkout/OpcSavePaymentMethod/`,
      {
        __RequestVerificationToken: checkoutToken,
        paymentmethod: PAYMENT_METHOD,
      }
    );
    const paymentJson = parseJson(paymentResponse.body, 'payment method response');
    if (paymentJson.error) {
      fail(`Payment method step failed: ${paymentResponse.body}`);
    }

    // Intentionally skip OpcSavePaymentInfo to force a server-side checkout failure.
    const confirmResponse = groupRequest('confirm order', 'POST', `${BASE_URL}/checkout/OpcConfirmOrder/`, {
      __RequestVerificationToken: checkoutToken,
    });
    const confirmJson = parseJson(confirmResponse.body, 'confirm response');

    check(confirmJson, {
      'checkout fails as expected': (body) => body.error === 1,
      'failure mentions missing payment info': (body) =>
        typeof body.message === 'string' && body.message.includes('Payment information is not entered'),
    });

    if (confirmJson.error !== 1) {
      fail(`Expected failed checkout, got: ${confirmResponse.body}`);
    }
  });

  sleep(PAUSE_SECONDS);
}

function groupRequest(label, method, url, body) {
  let response;
  group(label, () => {
    response = method === 'GET'
      ? http.get(url)
      : http.post(url, body, { headers: FORM_HEADERS });

    check(response, {
      [`${label} returned 200`]: (res) => res.status === 200,
    });
  });

  if (!response || response.status !== 200) {
    fail(`${label} failed with status ${response ? response.status : 'unknown'} at ${url}`);
  }

  return response;
}

function extractRequestVerificationToken(html, source) {
  return extractFirstMatch(
    html,
    /name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"/i,
    `${source} antiforgery token`,
    1
  );
}

function extractCheckoutAttributes(html) {
  const attributes = {};
  const selectRegex = /<select[^>]*name="(checkout_attribute_\d+)"[^>]*>([\s\S]*?)<\/select>/gi;
  let match;

  while ((match = selectRegex.exec(html)) !== null) {
    const fieldName = match[1];
    const selectedOrFirst = getSelectedOrFirstOptionFromMarkup(match[2]);
    if (selectedOrFirst) {
      attributes[fieldName] = selectedOrFirst;
    }
  }

  return attributes;
}

function getSelectedOrFirstOptionValue(html, fieldName) {
  const selectMatch = new RegExp(
    `<select[^>]*name="${escapeRegex(fieldName)}"[^>]*>([\\s\\S]*?)<\\/select>`,
    'i'
  ).exec(html);

  if (!selectMatch) {
    fail(`Could not find select "${fieldName}"`);
  }

  const value = getSelectedOrFirstOptionFromMarkup(selectMatch[1]);
  if (value === null) {
    fail(`Could not find a usable option for "${fieldName}"`);
  }

  return value;
}

function getSelectedOrFirstOptionFromMarkup(optionsMarkup) {
  const optionRegex = /<option([^>]*)value="([^"]+)"[^>]*>/gi;
  let firstNonZero = null;
  let selectedZero = null;
  let match;

  while ((match = optionRegex.exec(optionsMarkup)) !== null) {
    const attributes = match[1] || '';
    const value = match[2];
    if (value !== '0' && firstNonZero === null) {
      firstNonZero = value;
    }

    if (/selected/i.test(attributes) && value !== '0') {
      return value;
    }

    if (/selected/i.test(attributes) && value === '0') {
      selectedZero = value;
    }
  }

  return firstNonZero ?? selectedZero;
}

function extractCheckedOrFirstRadioValue(html, fieldName) {
  const radioRegex = new RegExp(
    `<input[^>]*type="radio"[^>]*name="${escapeRegex(fieldName)}"[^>]*value="([^"]+)"([^>]*)>`,
    'gi'
  );
  let firstValue = null;
  let match;

  while ((match = radioRegex.exec(html)) !== null) {
    const value = match[1];
    const trailingAttributes = match[2] || '';
    if (firstValue === null) {
      firstValue = value;
    }
    if (/checked/i.test(trailingAttributes)) {
      return value;
    }
  }

  if (firstValue === null) {
    fail(`Could not find radio field "${fieldName}"`);
  }

  return firstValue;
}

function extractFirstMatch(text, pattern, label, groupIndex = 0) {
  const match = pattern.exec(text);
  if (!match) {
    fail(`Could not extract ${label}`);
  }

  return match[groupIndex];
}

function parseJson(text, label) {
  try {
    return JSON.parse(text);
  } catch (error) {
    fail(`Could not parse ${label} as JSON: ${error}`);
  }
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
